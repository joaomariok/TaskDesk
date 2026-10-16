using Microsoft.Extensions.Logging;
using TaskDesk.Core.Paths;
using TaskDesk.Data.Logging;

namespace TaskDesk.Data.Tests;

public class LoggingTests
{
    [Fact]
    public void Log_files_are_written_to_the_logs_folder_and_named_by_day()
    {
        var root = Path.Combine(Path.GetTempPath(), "taskdesk-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.ForRoot(root);
            paths.EnsureDirectories();

            using (var factory = TaskDeskLogging.CreateLoggerFactory(paths))
            {
                factory.CreateLogger("test").LogInformation("hello");
            }

            var file = Assert.Single(Directory.GetFiles(paths.LogsDirectory));
            Assert.Matches(@"taskdesk-\d{8}\.log$", file);
            Assert.Contains("hello", File.ReadAllText(file));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
