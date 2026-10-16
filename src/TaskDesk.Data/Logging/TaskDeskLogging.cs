using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using TaskDesk.Core.Paths;

namespace TaskDesk.Data.Logging;

/// <summary>
/// File logging: one file per day under the logs folder, 14 days kept. Task contents must never be
/// logged at Information level; use ids there and titles only at Debug.
/// </summary>
public static class TaskDeskLogging
{
    public const int RetainedFileCount = 14;

    public static ILoggerFactory CreateLoggerFactory(IAppPaths paths)
        => new SerilogLoggerFactory(CreateSerilogLogger(paths), dispose: true);

    /// <summary>Routes all <c>Microsoft.Extensions.Logging</c> output to the daily log file.</summary>
    public static ILoggingBuilder AddTaskDeskFileLogging(this ILoggingBuilder builder, IAppPaths paths)
    {
        builder.ClearProviders();
        builder.AddProvider(new SerilogLoggerProvider(CreateSerilogLogger(paths), dispose: true));
        return builder;
    }

    private static Serilog.ILogger CreateSerilogLogger(IAppPaths paths)
        => new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Information)
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "taskdesk-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCount,
                shared: false)
            .CreateLogger();
}
