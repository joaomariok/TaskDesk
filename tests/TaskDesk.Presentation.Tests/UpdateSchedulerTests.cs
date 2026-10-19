using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation.Tests;

public class UpdateSchedulerTests
{
    private sealed record Setup(FakeTimeProvider Clock, FakeAppUpdates Updates, UpdateScheduler Scheduler);

    private static Setup Create(RuntimeMode? mode = null, bool installed = true)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        var updates = new FakeAppUpdates { IsInstalled = installed };
        return new Setup(clock, updates, new UpdateScheduler(updates, mode ?? RuntimeMode.Normal, clock));
    }

    [Fact]
    public async Task Starting_checks_at_once_and_again_after_each_interval()
    {
        var s = Create();

        s.Scheduler.Start();
        await s.Scheduler.CurrentCheck;
        Assert.Equal(1, s.Updates.Checks);

        s.Clock.Advance(UpdateScheduler.Interval - TimeSpan.FromMinutes(1));
        await s.Scheduler.CurrentCheck;
        Assert.Equal(1, s.Updates.Checks);

        s.Clock.Advance(TimeSpan.FromMinutes(1));
        await s.Scheduler.CurrentCheck;
        Assert.Equal(2, s.Updates.Checks);

        s.Clock.Advance(UpdateScheduler.Interval);
        await s.Scheduler.CurrentCheck;
        Assert.Equal(3, s.Updates.Checks);
    }

    [Fact]
    public async Task A_run_that_is_not_installed_never_checks()
    {
        var s = Create(installed: false);

        s.Scheduler.Start();
        await s.Scheduler.CurrentCheck;
        s.Clock.Advance(UpdateScheduler.Interval);
        await s.Scheduler.CurrentCheck;

        Assert.Equal(0, s.Updates.Checks);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public async Task A_redirected_data_folder_never_checks_even_with_the_sandbox_override(string? sandboxOverride)
    {
        var s = Create(RuntimeMode.Resolve(@"C:\temp\taskdesk", sandboxOverride));

        s.Scheduler.Start();
        await s.Scheduler.CurrentCheck;
        s.Clock.Advance(UpdateScheduler.Interval);
        await s.Scheduler.CurrentCheck;

        Assert.Equal(0, s.Updates.Checks);
    }

    [Fact]
    public async Task A_found_version_becomes_pending_and_raises_the_change_event()
    {
        var s = Create();
        s.Updates.Available = "1.1.0";
        var raised = new List<string?>();
        s.Scheduler.PendingVersionChanged += () => raised.Add(s.Scheduler.PendingVersion);

        s.Scheduler.Start();
        await s.Scheduler.CurrentCheck;

        Assert.Equal("1.1.0", s.Scheduler.PendingVersion);
        Assert.Equal(["1.1.0"], raised);
    }

    [Fact]
    public async Task Finding_the_same_version_again_raises_no_second_event()
    {
        var s = Create();
        s.Updates.Available = "1.1.0";
        var raised = 0;
        s.Scheduler.PendingVersionChanged += () => raised++;

        s.Scheduler.Start();
        await s.Scheduler.CurrentCheck;
        s.Clock.Advance(UpdateScheduler.Interval);
        await s.Scheduler.CurrentCheck;

        Assert.Equal(2, s.Updates.Checks);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task A_newer_version_found_later_replaces_the_pending_one()
    {
        var s = Create();
        s.Updates.Available = "1.1.0";
        s.Scheduler.Start();
        await s.Scheduler.CurrentCheck;

        s.Updates.Available = "1.2.0";
        s.Clock.Advance(UpdateScheduler.Interval);
        await s.Scheduler.CurrentCheck;

        Assert.Equal("1.2.0", s.Scheduler.PendingVersion);
    }

    [Fact]
    public async Task No_update_leaves_nothing_pending_and_raises_no_event()
    {
        var s = Create();
        var raised = 0;
        s.Scheduler.PendingVersionChanged += () => raised++;

        s.Scheduler.Start();
        await s.Scheduler.CurrentCheck;

        Assert.Equal(1, s.Updates.Checks);
        Assert.Null(s.Scheduler.PendingVersion);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task A_throwing_check_leaves_nothing_pending_and_is_retried_at_the_next_interval()
    {
        var s = Create();
        s.Updates.Available = "1.1.0";
        s.Updates.Failure = new HttpRequestException("offline");
        var raised = 0;
        s.Scheduler.PendingVersionChanged += () => raised++;

        s.Scheduler.Start();
        await s.Scheduler.CurrentCheck;

        Assert.Null(s.Scheduler.PendingVersion);
        Assert.Equal(0, raised);

        s.Updates.Failure = null;
        s.Clock.Advance(UpdateScheduler.Interval);
        await s.Scheduler.CurrentCheck;

        Assert.Equal(2, s.Updates.Checks);
        Assert.Equal("1.1.0", s.Scheduler.PendingVersion);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task A_failed_check_keeps_the_version_already_downloaded_pending()
    {
        var s = Create();
        s.Updates.Available = "1.1.0";
        s.Scheduler.Start();
        await s.Scheduler.CurrentCheck;

        s.Updates.Failure = new HttpRequestException("offline");
        s.Clock.Advance(UpdateScheduler.Interval);
        await s.Scheduler.CurrentCheck;

        Assert.Equal("1.1.0", s.Scheduler.PendingVersion);
    }

    [Fact]
    public async Task A_check_still_running_when_the_interval_elapses_is_not_overlapped()
    {
        var s = Create();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Updates.Gate = gate;
        s.Updates.Available = "1.1.0";

        s.Scheduler.Start();
        await s.Updates.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var first = s.Scheduler.CurrentCheck;
        s.Clock.Advance(UpdateScheduler.Interval);
        s.Clock.Advance(UpdateScheduler.Interval);

        Assert.Equal(1, s.Updates.Checks);
        Assert.Same(first, s.Scheduler.CurrentCheck);

        s.Updates.Gate = null;
        gate.SetResult();
        await first;
        Assert.Equal("1.1.0", s.Scheduler.PendingVersion);

        s.Clock.Advance(UpdateScheduler.Interval);
        await s.Scheduler.CurrentCheck;
        Assert.Equal(2, s.Updates.Checks);
    }

    [Fact]
    public async Task Disposing_cancels_the_running_check_and_stops_further_checks()
    {
        var s = Create();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Updates.Gate = gate;
        s.Updates.Available = "1.1.0";

        s.Scheduler.Start();
        await s.Updates.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var running = s.Scheduler.CurrentCheck;
        s.Scheduler.Dispose();
        await running;

        s.Clock.Advance(UpdateScheduler.Interval);
        await s.Scheduler.CurrentCheck;

        Assert.Equal(1, s.Updates.Checks);
        Assert.Null(s.Scheduler.PendingVersion);
    }

    [Fact]
    public void The_interval_is_six_hours()
    {
        Assert.Equal(TimeSpan.FromHours(6), UpdateScheduler.Interval);
    }
}

internal sealed class FakeAppUpdates : IAppUpdates
{
    private int _checks;

    public bool IsInstalled { get; set; } = true;

    public string CurrentVersion { get; set; } = "1.0.0";

    /// <summary>What the next checks return; null means no update.</summary>
    public string? Available { get; set; }

    /// <summary>Thrown by the next checks when set.</summary>
    public Exception? Failure { get; set; }

    /// <summary>When set, a check waits for it before it returns.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public int Checks => Volatile.Read(ref _checks);

    public int Restarts { get; private set; }

    /// <summary>Completed once a check has started.</summary>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<string?> CheckAndDownloadAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _checks);
        Entered.TrySetResult();
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        return Available;
    }

    public void RestartToApply() => Restarts++;
}
