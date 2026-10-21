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

    // The migration that adds "Notify at Due"; applying it to an existing database runs its upgrade rule.
    private const string NotifyAtDueMigration = "_Ticket118_NotifyAtDue";

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
        if (pending.Exists(m => m.EndsWith(NotifyAtDueMigration, StringComparison.Ordinal)))
        {
            await CountPastDuesAsNotifiedAsync(context, cancellationToken);
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

    // The upgrade rule of the Due notification (ReminderCalculator.CountPastDueAsNotified), run once when the
    // migration that turns "Notify at Due" on is applied, so the upgrade doesn't fire a pile of Overdue
    // notifications. It runs in memory: a Due moment depends on the time zone and the default Reminder time.
    private async Task CountPastDuesAsNotifiedAsync(TaskDeskDbContext context, CancellationToken cancellationToken)
    {
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
        logger.LogInformation("Counted {Count} Dues already past as notified", counted);
    }
}
