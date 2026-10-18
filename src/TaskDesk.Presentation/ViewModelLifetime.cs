namespace TaskDesk.Presentation;

/// <summary>
/// The one lifetime of a view model's deferred work: searches, reloads and timer work run through it, and it
/// ends once. After <see cref="StopAsync"/> no job starts, and a job the stop cancelled ends quietly.
/// </summary>
public sealed class ViewModelLifetime
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly HashSet<Task> _running = [];
    private readonly Lock _gate = new();
    private bool _stopped;

    /// <summary>True once <see cref="StopAsync"/> has been called.</summary>
    public bool IsStopped
    {
        get
        {
            lock (_gate)
            {
                return _stopped;
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="job"/> with a token cancelled by the caller's token or by the stop; does nothing once
    /// stopped. Once stopped, a job that ends in <see cref="OperationCanceledException"/> or
    /// <see cref="ObjectDisposedException"/> (its database let go of) ends quietly; before the stop, every failure
    /// propagates.
    /// </summary>
    public async Task RunAsync(Func<CancellationToken, Task> job, CancellationToken cancellationToken = default)
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _running.Add(ended.Task);
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
            await job(linked.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException && IsStopped)
        {
            // Nothing to show: the view model has stopped.
        }
        finally
        {
            lock (_gate)
            {
                _running.Remove(ended.Task);
            }

            ended.SetResult();
        }
    }

    /// <summary>
    /// Ends the lifetime: no job starts from now on, and the running ones are cancelled. The latch and the cancel
    /// happen before this returns. Idempotent; every call completes once the jobs running at the stop have ended,
    /// however they ended.
    /// </summary>
    public Task StopAsync() => Task.WhenAll(Latch());

    // Synchronous, so a caller that does not await the stop (a window's Closed) has still stopped every job.
    private Task[] Latch()
    {
        bool first;
        Task[] running;
        lock (_gate)
        {
            first = !_stopped;
            _stopped = true;
            running = [.. _running];
        }

        if (first)
        {
            _stopping.Cancel();
        }

        return running;
    }
}
