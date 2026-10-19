using TaskDesk.Core.Paths;

namespace TaskDesk.Presentation.Tests;

/// <summary>
/// The update downloaded by an earlier run is applied when the single-instance owner starts, before the host and
/// window are built, and the new version restarts with the same launch arguments.
/// </summary>
public class StartupUpdateTests
{
    private static FakeAppUpdates Pending(string? version = "1.1.0", bool installed = true) =>
        new() { IsInstalled = installed, DownloadedVersion = version };

    [Fact]
    public void A_downloaded_update_is_applied_and_restarts_with_the_launch_arguments()
    {
        var updates = Pending();

        var applied = StartupUpdate.ApplyIfPending(updates, RuntimeMode.Normal, launchedByNotification: false, ["--minimized"]);

        Assert.True(applied);
        Assert.Equal(1, updates.Restarts);
        Assert.Equal(["--minimized"], updates.RestartArguments);
    }

    [Fact]
    public void No_downloaded_update_starts_as_usual()
    {
        var updates = Pending(version: null);

        var applied = StartupUpdate.ApplyIfPending(updates, RuntimeMode.Normal, launchedByNotification: false, []);

        Assert.False(applied);
        Assert.Equal(0, updates.Restarts);
    }

    // A toast's Mark done or Snooze would be lost: the restarted process has no notification activation.
    [Fact]
    public void A_launch_from_a_notification_leaves_the_update_for_the_next_start()
    {
        var updates = Pending();

        var applied = StartupUpdate.ApplyIfPending(updates, RuntimeMode.Normal, launchedByNotification: true, []);

        Assert.False(applied);
        Assert.Equal(0, updates.Restarts);
    }

    [Fact]
    public void A_run_that_is_not_installed_never_applies()
    {
        var updates = Pending(installed: false);

        var applied = StartupUpdate.ApplyIfPending(updates, RuntimeMode.Normal, launchedByNotification: false, []);

        Assert.False(applied);
        Assert.Equal(0, updates.Restarts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public void A_redirected_data_folder_leaves_the_update_pending_even_with_the_sandbox_override(string? sandboxOverride)
    {
        var updates = Pending();

        var applied = StartupUpdate.ApplyIfPending(
            updates, RuntimeMode.Resolve(@"C:\temp\taskdesk", sandboxOverride), launchedByNotification: false, []);

        Assert.False(applied);
        Assert.Equal(0, updates.Restarts);
    }
}
