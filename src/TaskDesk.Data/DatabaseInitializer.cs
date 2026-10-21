using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskDesk.Core.Backups;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

/// <summary>Creates or migrates the database and seeds the Default List on first run.</summary>
public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

internal sealed class DatabaseInitializer(
    IDbContextFactory<TaskDeskDbContext> contextFactory,
    IAppPaths paths,
    IBackupService backups,
    TimeProvider clock,
    IShellSettingsStore settings,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    public const string DefaultListName = "Inbox";

    // The database's user_version once the upgrade rule of the Due notification has run. Kept in the file header,
    // not in a table, so it needs no migration; it is written in the same transaction as the rule's changes.
    private const long DueUpgradeDone = 1;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await InitializeCoreAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The database is corrupt or a migration failed");
            throw new DatabaseStartupException(paths.LogsDirectory, paths.BackupsDirectory, ex);
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        paths.EnsureDirectories();
        var existed = File.Exists(paths.DatabaseFile);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<string> pending = existed ? [.. await context.Database.GetPendingMigrationsAsync(cancellationToken)] : [];
        if (pending.Count > 0)
        {
            await backups.BackUpNowAsync(cancellationToken);
        }

        await context.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database migrated");
        if (await context.Database
                .SqlQueryRaw<long>("SELECT user_version AS \"Value\" FROM pragma_user_version")
                .SingleAsync(cancellationToken) < DueUpgradeDone)
        {
            try
            {
                await CountPastDuesAsNotifiedAsync(context, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Not fatal: the transaction rolled back, user_version stays unrecorded and the next start retries.
                logger.LogWarning(ex, "The upgrade rule of the Due notification failed; it will run again at the next start");
            }
        }

        try
        {
            await backups.BackUpIfDueAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The daily backup failed");
        }

        if (!await context.Lists.AnyAsync(l => l.IsDefault, cancellationToken))
        {
            context.Lists.Add(new TaskList
            {
                Id = Guid.CreateVersion7(),
                Name = DefaultListName,
                SortOrder = 0,
                IsDefault = true,
                CreatedUtc = clock.GetUtcNow().UtcDateTime,
            });
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded the Default List");
        }
    }

    // The upgrade rule of the Due notification (ReminderCalculator.CountPastDueAsNotified), run once after the
    // migration that turns "Notify at Due" on, so the upgrade doesn't fire a pile of Overdue notifications. Until it
    // completes, every start runs it again: a start stopped after the migration committed must not skip it. It runs
    // in memory: a Due moment depends on the time zone and the default Reminder time.
    private async Task CountPastDuesAsNotifiedAsync(TaskDeskDbContext context, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var rules = new MomentRules(clock.LocalTimeZone, settings.Load().DefaultReminderTime);
        var tasks = await context.Tasks
            .Include(t => t.Reminders)
            .Where(t => !t.IsCompleted && t.NotifyAtDue && t.DueDate != null)
            .ToListAsync(cancellationToken);
        foreach (var task in tasks)
        {
            ReminderCalculator.CountPastDueAsNotified(task, now, rules);
        }

        var counted = await context.SaveChangesAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA user_version = 1" /* DueUpgradeDone */, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Counted {Count} Dues already past as notified", counted);
    }
}
