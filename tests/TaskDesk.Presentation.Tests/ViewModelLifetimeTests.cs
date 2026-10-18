namespace TaskDesk.Presentation.Tests;

public sealed class ViewModelLifetimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stopping_twice_completes_both_times()
    {
        var lifetime = new ViewModelLifetime();

        await lifetime.StopAsync();
        await lifetime.StopAsync();

        Assert.True(lifetime.IsStopped);
    }

    [Fact]
    public async Task A_job_run_after_the_stop_does_not_start()
    {
        var lifetime = new ViewModelLifetime();
        await lifetime.StopAsync();
        var started = false;

        await lifetime.RunAsync(_ =>
        {
            started = true;
            return Task.CompletedTask;
        }, Ct);

        Assert.False(started);
    }

    [Fact]
    public async Task Stopping_while_a_job_runs_cancels_it_and_waits_for_it_to_end()
    {
        var lifetime = new ViewModelLifetime();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        var run = lifetime.RunAsync(async token =>
        {
            await release.Task;
            cancelled = token.IsCancellationRequested;
        }, Ct);

        var stop = lifetime.StopAsync();
        var second = lifetime.StopAsync();

        Assert.False(stop.IsCompleted, "The stop completed while its job still ran.");
        Assert.False(second.IsCompleted, "A second stop completed while the job still ran.");
        release.SetResult();
        await run;
        await stop;
        await second;
        Assert.True(cancelled, "The job's token was not cancelled by the stop.");
    }

    [Fact]
    public async Task The_stop_waits_for_a_job_that_fails()
    {
        var lifetime = new ViewModelLifetime();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = lifetime.RunAsync(async _ =>
        {
            await release.Task;
            throw new InvalidOperationException("boom");
        }, Ct);

        var stop = lifetime.StopAsync();
        Assert.False(stop.IsCompleted);
        release.SetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => run);
        await stop;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_job_cancelled_by_the_stop_ends_quietly_also_when_its_database_is_gone(bool disposed)
    {
        var lifetime = new ViewModelLifetime();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = lifetime.RunAsync(async token =>
        {
            await release.Task;
            if (disposed)
            {
                throw new ObjectDisposedException("IServiceProvider");
            }

            token.ThrowIfCancellationRequested();
        }, Ct);

        var stop = lifetime.StopAsync();
        release.SetResult();

        await run;
        await stop;
    }

    [Fact]
    public async Task A_job_cancelled_by_its_caller_before_the_stop_still_fails()
    {
        var lifetime = new ViewModelLifetime();
        using var caller = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = lifetime.RunAsync(async token =>
        {
            await release.Task;
            token.ThrowIfCancellationRequested();
        }, caller.Token);

        await caller.CancelAsync();
        release.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task A_failure_before_the_stop_propagates()
    {
        var lifetime = new ViewModelLifetime();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => lifetime.RunAsync(_ => throw new ObjectDisposedException("IServiceProvider"), Ct));
    }

    [Fact]
    public async Task The_job_sees_its_callers_cancellation()
    {
        var lifetime = new ViewModelLifetime();
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        var cancelled = false;

        await lifetime.RunAsync(token =>
        {
            cancelled = token.IsCancellationRequested;
            return Task.CompletedTask;
        }, caller.Token);

        Assert.True(cancelled);
    }
}
