using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskDesk.Core.Backups;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Reminders;
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
        services.AddSingleton<TaskChangeInterceptor>();
        services.AddSingleton<ITaskChangeNotifier>(sp => sp.GetRequiredService<TaskChangeInterceptor>());
        services.AddDbContextFactory<TaskDeskDbContext>((sp, options) => options
            // Pooling=False so the file is released as soon as a context is disposed.
            .UseSqlite($"Data Source={paths.DatabaseFile};Pooling=False")
            .AddInterceptors(sp.GetRequiredService<TaskChangeInterceptor>()));
        services.AddSingleton<IReminderService, ReminderService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<ITaskCreator>(sp => (TaskService)sp.GetRequiredService<ITaskService>());
        services.AddSingleton<IDueService, DueService>();
        services.AddSingleton<ITaskSearchService>(sp => (DueService)sp.GetRequiredService<IDueService>());
        services.AddSingleton<IRecurrenceService, RecurrenceService>();
        services.AddSingleton<ITagService, TagService>();
        services.AddSingleton<IListService, ListService>();
        return services;
    }
}
