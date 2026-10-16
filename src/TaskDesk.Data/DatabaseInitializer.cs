using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskDesk.Core.Backups;
using TaskDesk.Core.Paths;
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
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    public const string DefaultListName = "Inbox";

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

        if (existed && (await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
        {
            await backups.BackUpNowAsync(cancellationToken);
        }

        await context.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database migrated");

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
}
