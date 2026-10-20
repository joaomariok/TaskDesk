using TaskDesk.Core.Paths;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation.Tests;

/// <summary>
/// The decision made before the host exists: a launch that finds another copy forwards to it and ends; the
/// single-instance owner applies an update downloaded by an earlier run and restarts with the same launch arguments;
/// otherwise start-up carries on.
/// </summary>
public class LaunchGateTests
{
    private readonly FakeSingleInstance _single = new();

    private Task<LaunchDecision> DecideAsync(
        FakeAppUpdates updates, IReadOnlyList<string> launchArguments, NotificationActivation? launchActivation = null) =>
        LaunchGate.DecideAsync(
            _single, updates, RuntimeMode.Normal, launchArguments, launchActivation, TestContext.Current.CancellationToken);

    private static FakeAppUpdates Pending(string? version = "1.1.0", bool installed = true) =>
        new() { IsInstalled = installed, DownloadedVersion = version };

    [Fact]
    public void A_downloaded_update_is_applied_and_restarts_with_the_launch_arguments()
    {
        var updates = Pending();

        var applied = LaunchGate.ApplyIfPending(updates, RuntimeMode.Normal, launchedByNotification: false, ["--minimized"]);

        Assert.True(applied);
        Assert.Equal(1, updates.Restarts);
        Assert.Equal(["--minimized", LaunchGate.RestartedFlag], updates.RestartArguments);
    }

    // Update.exe restarts the app with the same arguments when applying fails too (a locked file, a corrupt
    // package), and the package is still pending: applying again would repeat at every start (review M3).
    [Fact]
    public void A_process_the_updater_restarted_does_not_apply_again()
    {
        var updates = Pending();

        var applied = LaunchGate.ApplyIfPending(
            updates, RuntimeMode.Normal, launchedByNotification: false, ["--minimized", LaunchGate.RestartedFlag]);

        Assert.False(applied);
        Assert.Equal(0, updates.Restarts);
    }

    [Fact]
    public void The_restart_arguments_mark_the_restart_and_keep_the_launch_arguments()
    {
        Assert.Equal(["--minimized", "--restarted-by-updater"], LaunchGate.RestartArguments(["--minimized"]));
        Assert.Equal(["--restarted-by-updater"], LaunchGate.RestartArguments([]));
    }

    [Fact]
    public void No_downloaded_update_starts_as_usual()
    {
        var updates = Pending(version: null);

        var applied = LaunchGate.ApplyIfPending(updates, RuntimeMode.Normal, launchedByNotification: false, []);

        Assert.False(applied);
        Assert.Equal(0, updates.Restarts);
    }

    // A toast's Mark done or Snooze would be lost: the restarted process has no notification activation.
    [Fact]
    public void A_launch_from_a_notification_leaves_the_update_for_the_next_start()
    {
        var updates = Pending();

        var applied = LaunchGate.ApplyIfPending(updates, RuntimeMode.Normal, launchedByNotification: true, []);

        Assert.False(applied);
        Assert.Equal(0, updates.Restarts);
    }

    [Fact]
    public void A_run_that_is_not_installed_never_applies()
    {
        var updates = Pending(installed: false);

        var applied = LaunchGate.ApplyIfPending(updates, RuntimeMode.Normal, launchedByNotification: false, []);

        Assert.False(applied);
        Assert.Equal(0, updates.Restarts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public void A_redirected_data_folder_leaves_the_update_pending_even_with_the_sandbox_override(string? sandboxOverride)
    {
        var updates = Pending();

        var applied = LaunchGate.ApplyIfPending(
            updates, RuntimeMode.Resolve(@"C:\temp\taskdesk", sandboxOverride), launchedByNotification: false, []);

        Assert.False(applied);
        Assert.Equal(0, updates.Restarts);
    }

    // Moved from ShellControllerTests (#99): the launch gate, not ShellController, finds the running copy. "Does not
    // set up" is now the outcome: the App builds the host, the tray and the Run key only to carry on.
    [Fact]
    public async Task Second_instance_signals_the_first_and_does_not_set_up()
    {
        _single.IsFirst = false;
        var updates = Pending(version: null);

        var decision = await DecideAsync(updates, []);

        Assert.Equal(LaunchOutcome.ForwardAndEnd, decision.Outcome);
        Assert.Equal(1, _single.SignalCount);
        Assert.Equal(0, updates.Restarts);
    }

    // Moved from ShellControllerTests (#99).
    [Fact]
    public async Task A_second_instance_forwards_its_minimized_flag()
    {
        _single.IsFirst = false;

        await DecideAsync(Pending(version: null), [SecondLaunchArguments.MinimizedFlag]);

        Assert.Equal([SecondLaunchArguments.MinimizedFlag], Assert.Single(_single.Signals));
    }

    // Moved from ShellControllerTests (#99): the launch activation and the background work belong to "carry on".
    [Fact]
    public async Task A_launch_that_finds_another_instance_runs_nothing_else()
    {
        _single.IsFirst = false;
        var updates = Pending();

        var decision = await DecideAsync(updates, [], NotificationActivation.OpenToday);

        Assert.Equal(LaunchOutcome.ForwardAndEnd, decision.Outcome);
        Assert.Equal(0, updates.Restarts);
    }

    [Fact]
    public async Task Nothing_applies_when_another_copy_runs()
    {
        _single.IsFirst = false;
        var updates = Pending();

        var decision = await DecideAsync(updates, []);

        Assert.Equal(LaunchOutcome.ForwardAndEnd, decision.Outcome);
        Assert.Equal(0, updates.Restarts);
        Assert.Null(decision.ApplyFailure);
    }

    [Fact]
    public async Task Forwarding_carries_the_minimized_flag_and_the_toast_activation()
    {
        _single.IsFirst = false;
        var markDone = new NotificationActivation(Guid.NewGuid(), ReminderAction.MarkDone);

        await DecideAsync(Pending(version: null), [SecondLaunchArguments.MinimizedFlag], markDone);

        Assert.Equal(new SecondLaunch(true, markDone), SecondLaunchArguments.Parse(Assert.Single(_single.Signals)));
    }

    [Fact]
    public async Task Forwarding_leaves_out_the_restarted_flag_and_releases_the_guard()
    {
        _single.IsFirst = false;

        await DecideAsync(Pending(version: null), [LaunchGate.RestartedFlag]);

        Assert.Empty(Assert.Single(_single.Signals));
        Assert.Equal(1, _single.DisposeCount);
    }

    [Fact]
    public async Task The_owner_applies_a_downloaded_update_and_restarts()
    {
        var updates = Pending();

        var decision = await DecideAsync(updates, [SecondLaunchArguments.MinimizedFlag]);

        Assert.Equal(LaunchOutcome.ApplyAndRestart, decision.Outcome);
        Assert.Equal(["--minimized", "--restarted-by-updater"], updates.RestartArguments);
        Assert.Equal(0, _single.SignalCount);
    }

    [Fact]
    public async Task With_no_downloaded_update_the_owner_carries_on_holding_the_guard()
    {
        var updates = Pending(version: null);

        var decision = await DecideAsync(updates, []);

        Assert.Equal(LaunchOutcome.CarryOn, decision.Outcome);
        Assert.Null(decision.ApplyFailure);
        Assert.Equal(0, _single.SignalCount);
        Assert.Equal(0, _single.DisposeCount);
    }

    [Fact]
    public async Task A_launch_from_a_toast_carries_on_and_leaves_the_update_pending()
    {
        var updates = Pending();

        var decision = await DecideAsync(updates, [], NotificationActivation.OpenToday);

        Assert.Equal(LaunchOutcome.CarryOn, decision.Outcome);
        Assert.Equal(0, updates.Restarts);
    }

    [Fact]
    public async Task A_failed_apply_carries_on_with_the_failure()
    {
        var failure = new IOException("locked");
        var updates = Pending();
        updates.ApplyFailure = failure;

        var decision = await DecideAsync(updates, []);

        Assert.Equal(LaunchOutcome.CarryOn, decision.Outcome);
        Assert.Same(failure, decision.ApplyFailure);
        Assert.Equal(0, _single.DisposeCount);
    }
}
