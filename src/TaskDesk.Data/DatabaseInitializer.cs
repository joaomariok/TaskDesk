using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    TimeProvider clock,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    public const string DefaultListName = "Inbox";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database migrated");

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
