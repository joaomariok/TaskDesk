using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

// The detail pane's saves go through one save queue: in the order they were made, landing after the stop when they
// were queued before it, and reporting a failed save as the pane's error.
public sealed class DetailSaveQueueTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test-3", TimeSpan.FromHours(-3), "Test-3", "Test-3");

    private const string Failure = "disk full";

    // The kinds of save, as the services see them.
    public const string Detail = "detail";
    public const string Due = "due";
    public const string Reminder = "reminder";
    public const string Recurrence = "recurrence";
    public const string SubtaskEdit = "subtask";
    public const string SubtaskMove = "subtask move";

    // Records every save that reaches a service, in order. The next save of one kind can be held until released,
    // and every save of one kind can be made to fail.
    private sealed class SaveLog
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Order { get; } = [];

        public string? HoldNext { get; set; }

        public string? Fail { get; set; }

        public void Release() => _released.SetResult();

        public async Task EnterAsync(string kind)
        {
            Order.Add(kind);
            if (HoldNext == kind)
            {
                HoldNext = null;
                await _released.Task;
            }

            if (Fail == kind)
            {
                throw new InvalidOperationException(Failure);
            }
        }
    }

    private sealed class LoggedTaskService(FakeTaskService inner, SaveLog log) : ITaskService
    {
        public async Task<TaskItem> UpdateTaskAsync(
            Guid taskId, string title, string? notes, Priority priority, CancellationToken cancellationToken = default)
        {
            await log.EnterAsync(Detail);
            return await inner.UpdateTaskAsync(taskId, title, notes, priority, cancellationToken);
        }

        public async Task<Subtask> RenameSubtaskAsync(
            Guid subtaskId, string title, CancellationToken cancellationToken = default)
        {
            await log.EnterAsync(SubtaskEdit);
            return await inner.RenameSubtaskAsync(subtaskId, title, cancellationToken);
        }

        public async Task<Subtask> SetSubtaskCompletedAsync(
            Guid subtaskId, bool isCompleted, CancellationToken cancellationToken = default)
        {
            await log.EnterAsync(SubtaskEdit);
            return await inner.SetSubtaskCompletedAsync(subtaskId, isCompleted, cancellationToken);
        }

        public async Task<IReadOnlyList<Subtask>> MoveSubtaskAsync(
            Guid subtaskId, int newIndex, CancellationToken cancellationToken = default)
        {
            await log.EnterAsync(SubtaskMove);
            return await inner.MoveSubtaskAsync(subtaskId, newIndex, cancellationToken);
        }

        public Task<Subtask> DeleteSubtaskAsync(Guid subtaskId, CancellationToken cancellationToken = default)
            => inner.DeleteSubtaskAsync(subtaskId, cancellationToken);

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

        public Task<DeletedTask> DeleteTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
            => inner.DeleteTaskAsync(taskId, cancellationToken);

        public Task<TaskItem> RestoreTaskAsync(DeletedTask deleted, CancellationToken cancellationToken = default)
            => inner.RestoreTaskAsync(deleted, cancellationToken);

        public Task<Subtask> AddSubtaskAsync(Guid taskId, string title, CancellationToken cancellationToken = default)
            => inner.AddSubtaskAsync(taskId, title, cancellationToken);

        public Task<Subtask> RestoreSubtaskAsync(Subtask deleted, CancellationToken cancellationToken = default)
            => inner.RestoreSubtaskAsync(deleted, cancellationToken);
    }

    private sealed class LoggedDueService(FakeTaskService tasks, SaveLog log) : IDueService
    {
        public async Task<TaskItem> SetDueAsync(
            Guid taskId, DateOnly? dueDate, TimeOnly? dueTime, CancellationToken cancellationToken = default)
        {
            await log.EnterAsync(Due);
            var task = tasks.All.Single(t => t.Id == taskId);
            task.DueDate = dueDate;
            task.DueTime = dueTime;
            return task;
        }

        public Task<IReadOnlyList<TaskItem>> ListSmartViewAsync(
            SmartView view, int skip = 0, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>([.. tasks.All.Where(t => !t.IsCompleted)]);

        public Task<SmartViewCounts> CountSmartViewsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SmartViewCounts(0, 0, 0, 0));
    }

    private sealed class LoggedRecurrenceService(FakeTaskService tasks, SaveLog log) : IRecurrenceService
    {
        public async Task<TaskItem> SetRecurrenceAsync(
            Guid taskId, Recurrence? recurrence, CancellationToken cancellationToken = default)
        {
            await log.EnterAsync(Recurrence);
            var task = tasks.All.Single(t => t.Id == taskId);
            task.Recurrence = recurrence;
            return task;
        }

        public Task<TaskItem> UndoCompletionAsync(
            Guid taskId, Guid? producedOccurrenceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SkippedOccurrence> SkipOccurrenceAsync(Guid taskId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TaskItem> UndoSkipAsync(SkippedOccurrence skipped, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class LoggedReminderService(FakeReminderService inner, SaveLog log) : IReminderService
    {
        public async Task<TaskItem> AddReminderAsync(
            Guid taskId, ReminderValue reminder, CancellationToken cancellationToken = default)
        {
            await log.EnterAsync(Reminder);
            return await inner.AddReminderAsync(taskId, reminder, cancellationToken);
        }

        public async Task<TaskItem> UpdateReminderAsync(
            Guid reminderId, ReminderValue reminder, CancellationToken cancellationToken = default)
        {
            await log.EnterAsync(Reminder);
            return await inner.UpdateReminderAsync(reminderId, reminder, cancellationToken);
        }

        public async Task<TaskItem> RemoveReminderAsync(Guid reminderId, CancellationToken cancellationToken = default)
        {
            await log.EnterAsync(Reminder);
            return await inner.RemoveReminderAsync(reminderId, cancellationToken);
        }

        public Task<TaskItem> SetNotifyAtDueAsync(
            Guid taskId, bool notifyAtDue, CancellationToken cancellationToken = default)
            => inner.SetNotifyAtDueAsync(taskId, notifyAtDue, cancellationToken);

        public Task<TaskItem> SnoozeAsync(Guid taskId, TimeSpan duration, CancellationToken cancellationToken = default)
            => inner.SnoozeAsync(taskId, duration, cancellationToken);

        public Task<TaskItem> ClearSnoozeAsync(Guid taskId, CancellationToken cancellationToken = default)
            => inner.ClearSnoozeAsync(taskId, cancellationToken);

        public Task<IReadOnlyList<DueReminder>> ListDueAsync(CancellationToken cancellationToken = default)
            => inner.ListDueAsync(cancellationToken);

        public Task<bool> TryMarkFiredAsync(Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default)
            => inner.TryMarkFiredAsync(taskId, dueAtUtc, cancellationToken);

        public Task<DateTime?> TryClaimAsync(Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default)
            => inner.TryClaimAsync(taskId, dueAtUtc, cancellationToken);

        public Task<bool> ReleaseClaimAsync(Guid taskId, DateTime firedUtc, CancellationToken cancellationToken = default)
            => inner.ReleaseClaimAsync(taskId, firedUtc, cancellationToken);
    }

    private sealed record Setup(FakeTaskService Store, SaveLog Log, FakeTimeProvider Clock, MainViewModel Vm);

    // One Task with a Due date (so it may repeat) and two Subtasks, shown in the pane. Local time is UTC-3.
    private static async Task<Setup> StartAsync()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(Zone);
        var store = new FakeTaskService();
        var task = await store.CreateTaskAsync("a", Ct);
        task.DueDate = new DateOnly(2026, 10, 20);
        await store.AddSubtaskAsync(task.Id, "one", Ct);
        await store.AddSubtaskAsync(task.Id, "two", Ct);
        var log = new SaveLog();
        var vm = new MainViewModel(
            new LoggedTaskService(store, log), new FakeTagService(store), clock,
            new LoggedDueService(store, log),
            recurrenceService: new LoggedRecurrenceService(store, log),
            reminderService: new LoggedReminderService(new FakeReminderService(clock, [.. store.All]), log));
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks[0];
        return new Setup(store, log, clock, vm);
    }

    // Makes one edit of the given kind in the pane; `second` picks a value different from the first edit's.
    // A title edit is debounced: it is queued when the delay ends, or when a flush or the stop saves it.
    private static void Edit(MainViewModel vm, string kind, bool second = false)
    {
        switch (kind)
        {
            case Detail:
                vm.DetailTitle = second ? "second" : "first";
                break;
            case Due:
                vm.DetailDueDate = new DateTime(2026, 10, second ? 22 : 21);
                break;
            case Reminder:
                vm.AddReminderCommand.Execute(null);
                vm.ReminderEditor.Date = new DateTime(2026, 10, second ? 22 : 21);
                vm.ReminderEditor.SaveCommand.Execute(null);
                break;
            case Recurrence:
                vm.Repeat.Preset = second ? RepeatPreset.Weekly : RepeatPreset.Daily;
                break;
            case SubtaskEdit:
                vm.SubtaskItems[0].Title = second ? "renamed again" : "renamed";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    public static TheoryData<string> Kinds => [Detail, Due, Reminder, Recurrence, SubtaskEdit];

    [Fact]
    public async Task Saves_of_different_kinds_run_in_the_order_they_were_made()
    {
        var s = await StartAsync();
        s.Log.HoldNext = Due;
        Edit(s.Vm, Due);
        Edit(s.Vm, Reminder);
        s.Vm.DetailPriority = Priority.High; // a title, notes and Priority save, not debounced
        Edit(s.Vm, SubtaskEdit);
        var move = s.Vm.MoveSubtaskUpCommand.ExecuteAsync(s.Vm.SubtaskItems[1]);
        Edit(s.Vm, Recurrence);
        Assert.Equal([Due], s.Log.Order);

        s.Log.Release();
        await s.Vm.PendingSave;
        await move;

        Assert.Equal([Due, Reminder, Detail, SubtaskEdit, SubtaskMove, Recurrence], s.Log.Order);
        Assert.Null(s.Vm.ErrorMessage);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_save_queued_behind_a_held_save_lands_after_the_stop(string kind)
    {
        var s = await StartAsync();
        var held = kind == Due ? Reminder : Due;
        s.Log.HoldNext = held;
        Edit(s.Vm, held);
        Edit(s.Vm, kind);

        var stop = s.Vm.StopAsync();
        Assert.False(stop.IsCompleted, "The stop completed while a save was held.");
        s.Log.Release();
        await stop;

        Assert.Equal([held, kind], s.Log.Order);
        Assert.Null(s.Vm.ErrorMessage);
        var stored = s.Store.All[0];
        switch (kind)
        {
            case Detail:
                Assert.Equal("first", stored.Title);
                break;
            case Due:
                Assert.Equal(new DateOnly(2026, 10, 21), stored.DueDate);
                break;
            case Recurrence:
                Assert.NotNull(stored.Recurrence);
                break;
            case SubtaskEdit:
                Assert.Equal("renamed", stored.Subtasks[0].Title);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_failed_save_shows_the_error_and_a_later_success_clears_it(string kind)
    {
        var s = await StartAsync();
        s.Log.Fail = kind;
        Edit(s.Vm, kind);
        s.Clock.Advance(MainViewModel.SaveDebounce);
        await s.Vm.PendingSave;

        Assert.Equal([kind], s.Log.Order);
        Assert.Equal("Could not save the changes: " + Failure, s.Vm.ErrorMessage);
        Assert.Equal("Could not save the changes: " + Failure, s.Vm.GeneralError);

        s.Log.Fail = null;
        Edit(s.Vm, kind, second: true);
        s.Clock.Advance(MainViewModel.SaveDebounce);
        await s.Vm.PendingSave;

        Assert.Equal([kind, kind], s.Log.Order);
        Assert.Null(s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task A_save_after_a_failed_save_still_runs()
    {
        var s = await StartAsync();
        s.Log.HoldNext = Due;
        s.Log.Fail = Due;
        Edit(s.Vm, Due);
        Edit(s.Vm, SubtaskEdit);

        s.Log.Release();
        await s.Vm.PendingSave;

        Assert.Equal([Due, SubtaskEdit], s.Log.Order);
        Assert.Equal("renamed", s.Store.All[0].Subtasks[0].Title);
        Assert.Null(s.Vm.ErrorMessage);
    }
}
