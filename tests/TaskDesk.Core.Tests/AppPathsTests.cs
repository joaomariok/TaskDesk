using TaskDesk.Core.Paths;

namespace TaskDesk.Core.Tests;

public class AppPathsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "taskdesk-paths-test");

    [Fact]
    public void ForRoot_places_every_location_under_the_root()
    {
        IAppPaths paths = AppPaths.ForRoot(Root);

        Assert.Equal(Root, paths.DataDirectory);
        Assert.Equal(Path.Combine(Root, "taskdesk.db"), paths.DatabaseFile);
        Assert.Equal(Path.Combine(Root, "settings.json"), paths.SettingsFile);
        Assert.Equal(Path.Combine(Root, "backups"), paths.BackupsDirectory);
        Assert.Equal(Path.Combine(Root, "logs"), paths.LogsDirectory);
    }

    [Fact]
    public void Default_lives_in_a_TaskDesk_folder_under_local_app_data()
    {
        IAppPaths paths = AppPaths.Default();

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskDesk");
        Assert.Equal(expected, paths.DataDirectory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_without_an_override_uses_local_app_data(string? overrideRoot)
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskDesk");

        Assert.Equal(expected, AppPaths.Resolve(overrideRoot).DataDirectory);
    }

    [Fact]
    public void Resolve_with_an_override_uses_that_folder()
    {
        IAppPaths paths = AppPaths.Resolve(Root);

        Assert.Equal(Root, paths.DataDirectory);
        Assert.Equal(Path.Combine(Root, "taskdesk.db"), paths.DatabaseFile);
    }

    [Fact]
    public void EnsureDirectories_creates_data_backups_and_logs_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), "taskdesk-" + Guid.NewGuid().ToString("N"));
        try
        {
            AppPaths.ForRoot(root).EnsureDirectories();

            Assert.True(Directory.Exists(Path.Combine(root, "backups")));
            Assert.True(Directory.Exists(Path.Combine(root, "logs")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
