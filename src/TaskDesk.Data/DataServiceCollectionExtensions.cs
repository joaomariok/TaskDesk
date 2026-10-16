using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskDesk.Core.Backups;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>Registers the SQLite context factory, the initializer and the Core use-case services.</summary>
    public static IServiceCollection AddTaskDeskData(this IServiceCollection services, IAppPaths paths)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddSingleton(paths);
        services.AddDbContextFactory<TaskDeskDbContext>(options =>
            // Pooling=False so the file is released as soon as a context is disposed.
            options.UseSqlite($"Data Source={paths.DatabaseFile};Pooling=False"));
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<ITaskCreator>(sp => (TaskService)sp.GetRequiredService<ITaskService>());
        services.AddSingleton<ITagService, TagService>();
        services.AddSingleton<IListService, ListService>();
        return services;
    }
}
