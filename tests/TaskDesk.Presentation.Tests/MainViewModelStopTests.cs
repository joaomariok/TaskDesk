using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

// After StopAsync the main view model does no more work: no load, no count refresh and no timer.
public sealed class MainViewModelStopTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test-3", TimeSpan.FromHours(-3), "Test-3", "Test-3");

    private static DateOnly Day(int day) => new(2026, 10, day);

    // Counts the calls that reach the Tags; every load lists them first. Can hold a load, then fail it as a
    // disposed database does.
    private sealed class CountingTagService(FakeTaskService tasks) : ITagService
    {
        private readonly FakeTagService _inner = new(tasks);
        private TaskCompletionSource? _hold;

        public int Calls { get; private set; }

        public void HoldThenFailAsDisposed() =>
            _hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _hold!.SetResult();

        public async Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (_hold is { } hold)
            {
                await hold.Task;
                throw new ObjectDisposedException("IServiceProvider");
            }

            return await _inner.ListTagsAsync(cancellationToken);
        }

        public Task<Tag> CreateTagAsync(string name, CancellationToken cancellationToken = default)
            => _inner.CreateTagAsync(name, cancellationToken);

        public Task<Tag> AddTagToTaskAsync(Guid taskId, string name, CancellationToken cancellationToken = default)
            => _inner.AddTagToTaskAsync(taskId, name, cancellationToken);

        public Task RemoveTagFromTaskAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default)
            => _inner.RemoveTagFromTaskAsync(taskId, tagId, cancellationToken);

        public Task DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default)
            => _inner.DeleteTagAsync(tagId, cancellationToken);

        public Task<IReadOnlyList<TaskItem>> ListTasksByTagAsync(Guid tagId, CancellationToken cancellationToken = default)
            => _inner.ListTasksByTagAsync(tagId, cancellationToken);
    }

    // Today and All by the Core predicates; counts every call.
    private sealed class CountingDueService(FakeTaskService tasks, FakeTimeProvider clock) : IDueService
    {
        public int Calls { get; private set; }

        public List<Guid> SetCalls { get; } = [];

        public Task<TaskItem> SetDueAsync(
            Guid taskId, DateOnly? dueDate, TimeOnly? dueTime, CancellationToken cancellationToken = default)
        {
            SetCalls.Add(taskId);
            var task = tasks.All.Single(t => t.Id == taskId);
            task.DueDate = dueDate;
            task.DueTime = dueTime;
            return Task.FromResult(task);
        }

        public Task<IReadOnlyList<TaskItem>> ListSmartViewAsync(
            SmartView view, int skip = 0, CancellationToken cancellationToken = default)
        {
            Calls++;
            var now = clock.GetUtcNow();
            return Task.FromResult<IReadOnlyList<TaskItem>>(view == SmartView.Today
                ? [.. tasks.All.Where(t => DueCalculator.IsInToday(t.DueDate, t.IsCompleted, now, clock.LocalTimeZone))]
                : [.. tasks.All.Where(t => !t.IsCompleted)]);
        }

        public Task<SmartViewCounts> CountSmartViewsAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new SmartViewCounts(1, 2, 3, 4));
        }
    }

    // Records every Recurrence save.
    private sealed class RecordingRecurrenceService(FakeTaskService tasks) : IRecurrenceService
    {
        public List<Guid> SetCalls { get; } = [];

        public Task<TaskItem> SetRecurrenceAsync(
            Guid taskId, Recurrence? recurrence, CancellationToken cancellationToken = default)
        {
            SetCalls.Add(taskId);
            var task = tasks.All.Single(t => t.Id == taskId);
            task.Recurrence = recurrence;
            return Task.FromResult(task);
        }

        public Task<TaskItem> UndoCompletionAsync(
            Guid taskId, Guid? producedOccurrenceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SkippedOccurrence> SkipOccurrenceAsync(Guid taskId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TaskItem> UndoSkipAsync(SkippedOccurrence skipped, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    // Wraps the fake Task service: a Subtask tick is held until released, and the Subtask ticks, deletes and
    // moves that reach it are recorded. A held delete waits for its own release.
    private sealed class GatedSubtaskService(FakeTaskService inner, bool holdDeletes = false) : ITaskService
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _deleteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<Guid> Ticks { get; } = [];

        public List<Guid> Deletes { get; } = [];

        public List<Guid> Moves { get; } = [];

        public void Release() => _gate.SetResult();

        public void ReleaseDelete() => _deleteGate.SetResult();

        public async Task<Subtask> SetSubtaskCompletedAsync(
            Guid subtaskId, bool isCompleted, CancellationToken cancellationToken = default)
        {
            Ticks.Add(subtaskId);
            await _gate.Task;
            return await inner.SetSubtaskCompletedAsync(subtaskId, isCompleted, cancellationToken);
        }

        public Task<IReadOnlyList<Subtask>> MoveSubtaskAsync(
            Guid subtaskId, int newIndex, CancellationToken cancellationToken = default)
        {
            Moves.Add(subtaskId);
            return inner.MoveSubtaskAsync(subtaskId, newIndex, cancellationToken);
        }

        public async Task<Subtask> DeleteSubtaskAsync(Guid subtaskId, CancellationToken cancellationToken = default)
        {
            Deletes.Add(subtaskId);
            if (holdDeletes)
            {
                await _deleteGate.Task;
            }

            return await inner.DeleteSubtaskAsync(subtaskId, cancellationToken);
        }

        public Task<TaskItem> CreateTaskAsync(string title, CancellationToken cancellationToken = default)
            => inner.CreateTaskAsync(title, cancellationToken);

        public Task<IReadOnlyList<TaskItem>> ListDefaultListTasksAsync(CancellationToken cancellationToken = default)
            => inner.ListDefaultListTasksAsync(cancellationToken);

        public Task<IReadOnlyList<TaskItem>> ListTasksInListAsync(Guid listId, CancellationToken cancellationToken = default)
            => inner.ListTasksInListAsync(listId, cancellationToken);

        public Task MoveTaskAsync(Guid taskId, int newIndex, CancellationToken cancellationToken = default)
            => inner.MoveTaskAsync(taskId, newIndex, cancellationToken);

        public Task<TaskItem> SetCompletedAsync(Guid taskId, bool isCompleted, CancellationToken cancellationToken = default)
            => inner.SetCompletedAsync(taskId, isCompleted, cancellationToken);

        public Task<TaskItem> UpdateTaskAsync(
            Guid taskId, string title, string? notes, Priority priority, CancellationToken cancellationToken = default)
            => inner.UpdateTaskAsync(taskId, title, notes, priority, cancellationToken);

        public Task<DeletedTask> DeleteTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
            => inner.DeleteTaskAsync(taskId, cancellationToken);

        public Task<TaskItem> RestoreTaskAsync(DeletedTask deleted, CancellationToken cancellationToken = default)
            => inner.RestoreTaskAsync(deleted, cancellationToken);

        public Task<Subtask> AddSubtaskAsync(Guid taskId, string title, CancellationToken cancellationToken = default)
            => inner.AddSubtaskAsync(taskId, title, cancellationToken);

        public Task<Subtask> RenameSubtaskAsync(Guid subtaskId, string title, CancellationToken cancellationToken = default)
            => inner.RenameSubtaskAsync(subtaskId, title, cancellationToken);

        public Task<Subtask> RestoreSubtaskAsync(Subtask deleted, CancellationToken cancellationToken = default)
            => inner.RestoreSubtaskAsync(deleted, cancellationToken);
    }

    // A selected Task with two Subtasks and the first one's tick held in flight.
    private static async Task<(GatedSubtaskService Service, MainViewModel Vm)> StartWithTickInFlightAsync()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(Zone);
        var fake = new FakeTaskService();
        var task = await fake.CreateTaskAsync("a", Ct);
        await fake.AddSubtaskAsync(task.Id, "one", Ct);
        await fake.AddSubtaskAsync(task.Id, "two", Ct);
        var gated = new GatedSubtaskService(fake);
        var vm = new MainViewModel(gated, new CountingTagService(fake), clock, new CountingDueService(fake, clock));
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks[0];
        vm.SubtaskItems[0].IsCompleted = true;
        Assert.False(vm.PendingSave.IsCompleted);
        return (gated, vm);
    }

    // A selected Task with two Subtasks, whose Subtask deletes are held in flight once they reach the service.
    private static async Task<(FakeTaskService Inner, GatedSubtaskService Service, MainViewModel Vm)>
        StartWithDeletesHeldAsync()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(Zone);
        var fake = new FakeTaskService();
        var task = await fake.CreateTaskAsync("a", Ct);
        await fake.AddSubtaskAsync(task.Id, "one", Ct);
        await fake.AddSubtaskAsync(task.Id, "two", Ct);
        var gated = new GatedSubtaskService(fake, holdDeletes: true);
        gated.Release(); // ticks are not held
        var vm = new MainViewModel(gated, new CountingTagService(fake), clock, new CountingDueService(fake, clock));
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks[0];
        return (fake, gated, vm);
    }

    private sealed record Setup(
        FakeTaskService Service, CountingTagService Tags, CountingDueService Due, FakeTimeProvider Clock, MainViewModel Vm)
    {
        public RecordingRecurrenceService Recurrence { get; init; } = null!;

        public FakeReminderService Reminders { get; init; } = null!;
    }

    // Local time is UTC-3: 2026-10-20 10:00.
    private static async Task<Setup> StartAsync(params (string Title, DateOnly? Date, TimeOnly? Time)[] tasks)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(Zone);
        var service = new FakeTaskService();
        foreach (var (title, date, time) in tasks)
        {
            var task = await service.CreateTaskAsync(title, Ct);
            task.DueDate = date;
            task.DueTime = time;
        }

        var tags = new CountingTagService(service);
        var due = new CountingDueService(service, clock);
        var recurrence = new RecordingRecurrenceService(service);
        var reminders = new FakeReminderService(clock, [.. service.All]);
        var vm = new MainViewModel(
            service, tags, clock, due, recurrenceService: recurrence, reminderService: reminders);
        await vm.LoadAsync(Ct);
        return new Setup(service, tags, due, clock, vm) { Recurrence = recurrence, Reminders = reminders };
    }

    private static Task<Setup> StartAsync(params string[] titles) =>
        StartAsync([.. titles.Select(t => (t, (DateOnly?)null, (TimeOnly?)null))]);

    private static string[] Titles(IEnumerable<TaskRowViewModel> rows) => [.. rows.Select(r => r.Title)];

    [Fact]
    public async Task After_the_stop_a_load_touches_no_service_and_keeps_the_rows()
    {
        var s = await StartAsync("a");
        await s.Vm.StopAsync();
        var calls = (s.Tags.Calls, s.Due.Calls);
        await s.Service.CreateTaskAsync("b", Ct);

        await s.Vm.LoadAsync(Ct);

        Assert.Equal(calls, (s.Tags.Calls, s.Due.Calls));
        Assert.Equal(["a"], Titles(s.Vm.Tasks));
    }

    [Fact]
    public async Task After_the_stop_the_Smart_View_counts_are_not_refreshed()
    {
        var s = await StartAsync("a");
        await s.Vm.StopAsync();

        await s.Vm.RefreshSmartViewCountsAsync(Ct);

        Assert.Equal(0, s.Due.Calls);
        Assert.Equal(0, s.Vm.AllCount);
    }

    [Fact]
    public async Task After_the_stop_local_midnight_runs_no_refresh()
    {
        var s = await StartAsync(("today", Day(20), null), ("tomorrow", Day(21), null));
        await s.Vm.ShowSmartViewAsync(SmartView.Today, Ct);
        await s.Vm.StopAsync();
        var calls = (s.Tags.Calls, s.Due.Calls);

        s.Clock.Advance(TimeSpan.FromHours(14)); // local midnight
        await s.Vm.DueRefresh;

        Assert.Equal(calls, (s.Tags.Calls, s.Due.Calls));
        Assert.Equal(["today"], Titles(s.Vm.Tasks));
        Assert.Empty(s.Vm.OverdueTasks);
    }

    [Fact]
    public async Task After_the_stop_a_timed_Due_passing_runs_no_refresh()
    {
        var s = await StartAsync(("call", Day(20), new TimeOnly(10, 30)));
        var row = s.Vm.Tasks.Single();
        await s.Vm.StopAsync();

        s.Clock.Advance(TimeSpan.FromMinutes(31));
        await s.Vm.DueRefresh;

        Assert.False(row.IsOverdue, "The row was refreshed after the stop.");
    }

    [Fact]
    public async Task After_the_stop_the_Undo_bar_timer_runs_nothing()
    {
        var s = await StartAsync("a", "b");
        await s.Vm.DeleteCommand.ExecuteAsync(s.Vm.Tasks[0]);
        Assert.True(s.Vm.IsUndoBarVisible);

        await s.Vm.StopAsync();
        s.Clock.Advance(MainViewModel.UndoBarDuration);
        await s.Vm.UndoBarExpiry;

        Assert.True(s.Vm.IsUndoBarVisible, "The Undo bar timer ran after the stop.");
    }

    [Fact]
    public async Task After_the_stop_the_completion_hold_moves_nothing()
    {
        var s = await StartAsync("a");
        s.Vm.CompletionMoveDelay = () => TimeSpan.FromMilliseconds(400);
        var row = s.Vm.Tasks.Single();
        await s.Vm.ToggleCompleteCommand.ExecuteAsync(row);

        await s.Vm.StopAsync();
        s.Clock.Advance(TimeSpan.FromMilliseconds(400));
        await s.Vm.CompletionMove;

        Assert.True(row.IsCompleted);
        Assert.Same(row, s.Vm.Tasks.Single());
        Assert.Empty(s.Vm.CompletedTasks);
    }

    [Fact]
    public async Task A_load_the_stop_cancelled_ends_quietly_when_its_database_is_then_gone()
    {
        var s = await StartAsync("a");
        s.Tags.HoldThenFailAsDisposed();

        var load = s.Vm.LoadAsync(Ct);
        var stop = s.Vm.StopAsync();
        Assert.False(stop.IsCompleted, "The stop completed before the load it cancelled had ended.");
        s.Tags.Release();

        await load;
        await stop;
    }

    [Fact]
    public async Task A_load_whose_database_is_gone_still_fails_when_nothing_stopped_it()
    {
        var s = await StartAsync("a");
        s.Tags.HoldThenFailAsDisposed();

        var load = s.Vm.LoadAsync(Ct);
        s.Tags.Release();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => load);
    }

    [Fact]
    public async Task The_stop_saves_a_pending_detail_edit_before_it_completes_and_a_second_stop_saves_nothing_more()
    {
        var s = await StartAsync("a");
        var a = s.Vm.Tasks[0];
        s.Vm.SelectedTask = a;
        s.Vm.DetailTitle = "renamed";

        await s.Vm.StopAsync();

        Assert.Equal([(a.Id, "renamed", (string?)"", Priority.None)], s.Service.Updates);
        await s.Vm.StopAsync();
        s.Clock.Advance(MainViewModel.SaveDebounce);
        await s.Vm.PendingSave;
        Assert.Single(s.Service.Updates);
    }

    // Queues every continuation instead of running it, so a test decides when a resumed await runs.
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public int Count => _queue.Count;

        public void RunQueued()
        {
            while (_queue.TryDequeue(out var item))
            {
                item.Callback(item.State);
            }
        }
    }

    [Fact]
    public async Task The_stop_saves_an_edit_made_after_an_earlier_save_debounce_ended_but_before_it_resumed()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        var context = new QueuedContext();
        var original = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            s.Vm.DetailTitle = "first";

            // The first delay ends; its continuation is queued. A continuation runs inline when its context is the
            // completing thread's current one, so the clock advances from outside the queued context.
            SynchronizationContext.SetSynchronizationContext(null);
            s.Clock.Advance(MainViewModel.SaveDebounce);
            SynchronizationContext.SetSynchronizationContext(context);
            Assert.True(context.Count > 0);
            Assert.Empty(s.Service.Updates);
            s.Vm.DetailTitle = "second"; // arms the next debounce before the first save resumes
            context.RunQueued(); // the first save resumes

            var stop = s.Vm.StopAsync();

            // The lifetime completes its jobs on the thread pool; keep resuming the queued continuations until
            // the stop is done. Nothing here waits for a clock: the guard only stops a hung stop from hanging us.
            var guard = System.Diagnostics.Stopwatch.StartNew();
            while (!stop.IsCompleted && guard.Elapsed < TimeSpan.FromSeconds(30))
            {
                context.RunQueued();
                Thread.Yield();
            }

            Assert.True(stop.IsCompleted);
            await stop;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }

        Assert.Equal(["first", "second"], s.Service.Updates.Select(u => u.Title));
    }

    [Fact]
    public async Task After_the_stop_a_detail_edit_starts_no_save_debounce()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        await s.Vm.StopAsync();

        s.Vm.DetailTitle = "renamed";

        Assert.True(s.Vm.PendingSave.IsCompleted, "A save debounce was armed after the stop.");
        s.Clock.Advance(MainViewModel.SaveDebounce);
        await s.Vm.PendingSave;
        Assert.Empty(s.Service.Updates);
    }

    [Fact]
    public async Task After_the_stop_a_Subtask_edit_touches_no_service()
    {
        var s = await StartAsync("a");
        await s.Service.AddSubtaskAsync(s.Service.All[0].Id, "step", Ct);
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        await s.Vm.StopAsync();

        s.Vm.SubtaskItems[0].Title = "renamed";
        s.Vm.SubtaskItems[0].IsCompleted = true;
        await s.Vm.PendingSave;

        Assert.Equal("step", s.Service.All[0].Subtasks[0].Title);
        Assert.False(s.Service.All[0].Subtasks[0].IsCompleted, "A Subtask tick was saved after the stop.");
        Assert.Null(s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task A_Subtask_delete_waiting_on_a_save_does_nothing_once_the_stop_has_ended()
    {
        var (service, vm) = await StartWithTickInFlightAsync();
        var delete = vm.DeleteSubtaskCommand.ExecuteAsync(vm.SubtaskItems[0]);
        Assert.False(delete.IsCompleted);

        var stop = vm.StopAsync();
        service.Release();
        await stop;
        await delete;

        Assert.Empty(service.Deletes);
        Assert.Equal(2, vm.SubtaskItems.Count);
    }

    [Fact]
    public async Task A_Subtask_move_waiting_on_a_save_does_nothing_once_the_stop_has_ended()
    {
        var (service, vm) = await StartWithTickInFlightAsync();
        var move = vm.MoveSubtaskDownCommand.ExecuteAsync(vm.SubtaskItems[0]);
        Assert.False(move.IsCompleted);

        var stop = vm.StopAsync();
        service.Release();
        await stop;
        await move;

        Assert.Empty(service.Moves);
    }

    [Fact]
    public async Task The_stop_waits_for_a_Subtask_delete_in_flight_to_land()
    {
        var (inner, service, vm) = await StartWithDeletesHeldAsync();
        var gone = vm.SubtaskItems[0].Id;
        var delete = vm.DeleteSubtaskCommand.ExecuteAsync(vm.SubtaskItems[0]);
        Assert.Equal([gone], service.Deletes);

        var stop = vm.StopAsync();
        Assert.False(stop.IsCompleted);

        service.ReleaseDelete();
        await stop;
        Assert.True(delete.IsCompleted);
        var tasks = await inner.ListDefaultListTasksAsync(Ct);
        Assert.DoesNotContain(tasks[0].Subtasks, s => s.Id == gone);
    }

    [Fact]
    public async Task A_save_queued_after_a_Subtask_delete_waits_for_the_delete()
    {
        var (_, service, vm) = await StartWithDeletesHeldAsync();
        var delete = vm.DeleteSubtaskCommand.ExecuteAsync(vm.SubtaskItems[0]);
        var ticked = vm.SubtaskItems[1].Id;

        vm.SubtaskItems[1].IsCompleted = true;
        Assert.Empty(service.Ticks);

        service.ReleaseDelete();
        await delete;
        await vm.PendingSave;
        Assert.Equal([ticked], service.Ticks);
    }

    [Fact]
    public async Task Preparing_for_an_import_waits_for_a_Subtask_delete_ahead_of_a_debounced_edit()
    {
        var (inner, service, vm) = await StartWithDeletesHeldAsync();
        var gone = vm.SubtaskItems[0].Id;
        var delete = vm.DeleteSubtaskCommand.ExecuteAsync(vm.SubtaskItems[0]);
        vm.DetailNotes = "typed";

        // The superseded debounce ends on another thread, so give the chain a moment to run ahead if it would.
        var prepare = vm.PrepareForImportAsync();
        var first = await Task.WhenAny(prepare, Task.Delay(TimeSpan.FromMilliseconds(250), Ct));
        Assert.False(prepare.IsCompleted, "The import went ahead while a Subtask delete was in flight.");
        Assert.NotSame(prepare, first);

        service.ReleaseDelete();
        await prepare;
        Assert.True(delete.IsCompleted);
        var tasks = await inner.ListDefaultListTasksAsync(Ct);
        Assert.DoesNotContain(tasks[0].Subtasks, s => s.Id == gone);
        Assert.Equal("typed", tasks[0].Notes);
    }

    [Fact]
    public async Task A_Subtask_move_queued_behind_its_delete_does_nothing()
    {
        var (service, vm) = await StartWithTickInFlightAsync();
        var gone = vm.SubtaskItems[1];
        var delete = vm.DeleteSubtaskCommand.ExecuteAsync(gone);
        var move = vm.MoveSubtaskUpCommand.ExecuteAsync(gone);

        service.Release();
        await delete;
        await move;

        Assert.Equal([gone.Id], service.Deletes);
        Assert.Empty(service.Moves);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task After_the_stop_a_Due_change_touches_no_service()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        await s.Vm.StopAsync();

        s.Vm.DetailDueDate = new DateTime(2026, 10, 21);
        await s.Vm.PendingSave;

        Assert.Empty(s.Due.SetCalls);
        Assert.Null(s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task After_the_stop_a_Recurrence_change_touches_no_service()
    {
        var s = await StartAsync(("a", Day(20), null));
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        await s.Vm.StopAsync();

        s.Vm.Repeat.Preset = RepeatPreset.Daily;
        await s.Vm.PendingSave;

        Assert.Empty(s.Recurrence.SetCalls);
        Assert.Null(s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task After_the_stop_a_detail_Reminder_change_touches_no_service()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        await s.Vm.StopAsync();

        s.Vm.DetailReminderDate = new DateTime(2026, 10, 21);
        await s.Vm.PendingSave;

        Assert.Empty(s.Reminders.SetCalls);
        Assert.Null(s.Vm.ErrorMessage);
    }
}
