namespace TaskDesk.Presentation;

/// <summary>
/// The one queue of the detail pane's saves, owned by <see cref="MainViewModel"/> and built on its lifetime and
/// clock. Every save runs once each save queued before it has ended, however that ended, so saves of one Task never
/// overlap; a save never awaits the queue itself. The queue also owns the debounced title, notes and Priority edit,
/// and turns a failed save into the pane's error.
/// <para>
/// The stop rule, stated once: once the lifetime has stopped the queue accepts nothing. Of the work already queued
/// at the stop, a save still lands (the stop waits for it), and a Subtask delete or move is dropped, because it runs
/// through the lifetime.
/// </para>
/// </summary>
internal sealed class DetailSaveQueue(
    ViewModelLifetime lifetime, TimeProvider clock, TimeSpan debounce, Action<string?> showError)
{
    private const string SaveFailed = "Could not save the changes: ";

    // Ends once every save queued so far has ended; never faults.
    private Task _chain = Task.CompletedTask;

    // The debounced edit waiting for its delay: whose it is, the cancel of its delay, and what it stores.
    private PendingEdit? _pending;

    private sealed record PendingEdit(Guid TaskId, CancellationTokenSource Delay, Func<Task<bool>> Store);

    /// <summary>
    /// Queues a save. <paramref name="store"/> stores the change and returns false when it stored nothing (such as an
    /// invalid value), which leaves the error as it is; a stored change clears the error, a failure shows it.
    /// <paramref name="accepted"/> runs once the queue has taken the save and before it can start: the place for what
    /// the pane shows at once. Once stopped nothing is queued, neither runs, and this returns null; otherwise it
    /// returns the save, which never faults.
    /// </summary>
    public Task? Enqueue(Func<Task<bool>> store, Action? accepted = null)
    {
        if (lifetime.IsStopped)
        {
            return null;
        }

        accepted?.Invoke();
        return Append(previous => SaveAfterAsync(previous, store));
    }

    /// <summary>
    /// Queues a Subtask delete or move. Its failure goes to the returned task (the command), not to the error, and
    /// the queue carries on past it. Queued before the stop, it does nothing once the stop has come; once stopped,
    /// nothing is queued.
    /// </summary>
    public Task EnqueueSubtaskChange(Func<Task> change)
        => lifetime.IsStopped ? Task.CompletedTask : Append(previous => ChangeAfterAsync(previous, change));

    /// <summary>
    /// Schedules the title, notes and Priority edit of a Task, replacing the pending one, which then stores nothing.
    /// Immediate, it is queued now; otherwise its delay starts at once on the clock, and it is queued in its place,
    /// so a later save waits for it.
    /// </summary>
    public void ScheduleEdit(Guid taskId, Func<Task<bool>> store, bool immediate)
    {
        CancelPending();
        if (immediate)
        {
            _ = Enqueue(store);
            return;
        }

        if (lifetime.IsStopped)
        {
            return;
        }

        var pending = new PendingEdit(taskId, new CancellationTokenSource(), store);
        _pending = pending;
        _ = Append(previous => DebouncedSaveAsync(previous, pending));
    }

    /// <summary>Saves the pending edit now.</summary>
    public void Flush()
    {
        if (_pending is { } pending)
        {
            ScheduleEdit(pending.TaskId, pending.Store, immediate: true);
        }
    }

    /// <summary>Drops the pending edit when it belongs to <paramref name="taskId"/>; another Task's edit stays.</summary>
    public void DropPendingEdit(Guid taskId)
    {
        if (_pending?.TaskId == taskId)
        {
            CancelPending();
        }
    }

    /// <summary>Completes once every save queued so far has ended, however it ended.</summary>
    public Task WaitForAllAsync() => _chain;

    private void CancelPending()
    {
        _pending?.Delay.Cancel();
        _pending = null;
    }

    // The link starts now, given the end of the queue so far; the queue moves on once both have ended.
    private Task Append(Func<Task, Task> link)
    {
        var previous = _chain;
        var started = link(previous);
        _chain = ChainEndAsync(previous, started);
        return started;
    }

    private static async Task ChainEndAsync(Task previous, Task link)
    {
        try
        {
            await Task.WhenAll(previous, link);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A Subtask change carries its failure to its command; later saves still run.
        }
    }

    private async Task SaveAfterAsync(Task previous, Func<Task<bool>> store)
    {
        await previous;
        try
        {
            if (await store())
            {
                showError(null);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            showError(SaveFailed + ex.Message);
        }
    }

    // A change already queued at the stop runs through the lifetime, so it starts nothing once stopped. It keeps
    // its command's token, not the stop's: a change in flight at the stop lands, as a save does.
    private async Task ChangeAfterAsync(Task previous, Func<Task> change)
    {
        await previous;
        await lifetime.RunAsync(_ => change());
    }

    private async Task DebouncedSaveAsync(Task previous, PendingEdit pending)
    {
        // The delay starts before anything is awaited, so the timer exists as soon as the edit is made. It runs
        // through the lifetime: a stop ends it quietly, and once stopped it is never armed.
        var waited = false;
        try
        {
            await lifetime.RunAsync(
                async cancellationToken =>
                {
                    await Task.Delay(debounce, clock, cancellationToken);
                    waited = true;
                },
                pending.Delay.Token);
        }
        catch (OperationCanceledException)
        {
            // replaced by a later edit, flushed, or dropped
        }

        if (!waited)
        {
            return; // replaced, flushed, dropped, or the view model stopped: store nothing
        }

        // A later edit may be pending already; clear only this one.
        if (_pending == pending)
        {
            _pending = null;
        }

        await SaveAfterAsync(previous, pending.Store);
    }
}
