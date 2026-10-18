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

        public Task<TaskItem> SetDueAsync(
            Guid taskId, DateOnly? dueDate, TimeOnly? dueTime, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

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

    private sealed record Setup(
        FakeTaskService Service, CountingTagService Tags, CountingDueService Due, FakeTimeProvider Clock, MainViewModel Vm);

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
        var vm = new MainViewModel(service, tags, clock, due);
        await vm.LoadAsync(Ct);
        return new Setup(service, tags, due, clock, vm);
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
}
