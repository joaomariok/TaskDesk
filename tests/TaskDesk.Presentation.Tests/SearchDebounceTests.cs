using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

// The search waits for typing to pause, on the view model's clock, and stops with the view model.
public sealed class SearchDebounceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    // Counts the loads: every load lists the Tags first.
    private sealed class CountingTagService(ITagService inner) : ITagService
    {
        public int Loads { get; private set; }

        public Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken cancellationToken = default)
        {
            Loads++;
            return inner.ListTagsAsync(cancellationToken);
        }

        public Task<Tag> CreateTagAsync(string name, CancellationToken cancellationToken = default)
            => inner.CreateTagAsync(name, cancellationToken);

        public Task<Tag> AddTagToTaskAsync(Guid taskId, string name, CancellationToken cancellationToken = default)
            => inner.AddTagToTaskAsync(taskId, name, cancellationToken);

        public Task RemoveTagFromTaskAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default)
            => inner.RemoveTagFromTaskAsync(taskId, tagId, cancellationToken);

        public Task DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default)
            => inner.DeleteTagAsync(tagId, cancellationToken);

        public Task<IReadOnlyList<TaskItem>> ListTasksByTagAsync(Guid tagId, CancellationToken cancellationToken = default)
            => inner.ListTasksByTagAsync(tagId, cancellationToken);
    }

    // Matches on the title only.
    private sealed class FakeSearchService(FakeTaskService tasks) : ITaskSearchService
    {
        public List<string?> Texts { get; } = [];

        public Task<IReadOnlyList<TaskItem>> SearchAsync(
            TaskSearchQuery query, CancellationToken cancellationToken = default)
        {
            Texts.Add(query.Text);
            return Task.FromResult<IReadOnlyList<TaskItem>>(
                [.. tasks.All.Where(t => t.Title.Contains((query.Text ?? "").Trim(), StringComparison.OrdinalIgnoreCase))]);
        }
    }

    // The All Smart View: every open Task.
    private sealed class AllViewDueService(FakeTaskService tasks) : IDueService
    {
        public Task<TaskItem> SetDueAsync(
            Guid taskId, DateOnly? dueDate, TimeOnly? dueTime, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TaskItem>> ListSmartViewAsync(
            SmartView view, int skip = 0, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>([.. tasks.All.Where(t => !t.IsCompleted)]);

        public Task<SmartViewCounts> CountSmartViewsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed record Setup(
        FakeTaskService Service, CountingTagService Tags, FakeSearchService Search, FakeTimeProvider Clock, MainViewModel Vm);

    private static async Task<Setup> StartAsync(params string[] titles)
    {
        var service = new FakeTaskService();
        foreach (var title in titles)
        {
            await service.CreateTaskAsync(title, Ct);
        }

        var tags = new CountingTagService(new FakeTagService(service));
        var search = new FakeSearchService(service);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        var vm = new MainViewModel(
            service, tags, clock, dueService: new AllViewDueService(service), searchService: search);
        await vm.LoadAsync(Ct);
        return new Setup(service, tags, search, clock, vm);
    }

    private static async Task SearchRunsOutAsync(Setup s)
    {
        s.Clock.Advance(MainViewModel.SearchDelay);
        if (s.Vm.ApplySearchCommand.ExecutionTask is { } run)
        {
            await run;
        }
    }

    [Fact]
    public async Task Typing_searches_once_after_typing_pauses_for_the_search_delay()
    {
        var s = await StartAsync("Buy milk", "Walk dog");

        s.Vm.SearchText = "m";
        s.Clock.Advance(MainViewModel.SearchDelay - Tick);
        s.Vm.SearchText = "mi";
        s.Clock.Advance(MainViewModel.SearchDelay - Tick);
        s.Vm.SearchText = "milk";
        s.Clock.Advance(MainViewModel.SearchDelay - Tick);

        Assert.Empty(s.Search.Texts);
        Assert.True(s.Vm.IsSearchPending, "Typing left no search waiting.");

        s.Clock.Advance(Tick);
        await s.Vm.ApplySearchCommand.ExecutionTask!;

        Assert.Equal(["milk"], s.Search.Texts);
        Assert.Equal(["Buy milk"], s.Vm.Tasks.Select(t => t.Title));
        Assert.False(s.Vm.IsSearchPending, "The search still waits after it ran.");
    }

    [Fact]
    public async Task Clear_search_reloads_once_and_leaves_no_search_waiting()
    {
        var s = await StartAsync("Buy milk", "Walk dog");
        s.Vm.SearchText = "milk";
        await SearchRunsOutAsync(s);
        var loads = s.Tags.Loads;

        await s.Vm.ClearSearchCommand.ExecuteAsync(null);

        Assert.False(s.Vm.IsSearchPending, "Clear search left a search waiting.");
        s.Clock.Advance(MainViewModel.SearchDelay);
        Assert.Equal(loads + 1, s.Tags.Loads);
        Assert.Equal(["Buy milk", "Walk dog"], s.Vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Opening_a_List_while_typing_loads_once_and_leaves_no_search_waiting()
    {
        var s = await StartAsync("Buy milk", "Walk dog");
        var listId = s.Service.All[0].ListId;
        var loads = s.Tags.Loads;

        s.Vm.SearchText = "milk";
        await s.Vm.ShowListAsync(listId, Ct);

        Assert.False(s.Vm.IsSearchPending, "Opening a List left a search waiting.");
        s.Clock.Advance(MainViewModel.SearchDelay);
        Assert.Equal(loads + 1, s.Tags.Loads);
        Assert.Empty(s.Search.Texts);
    }

    [Fact]
    public async Task Opening_a_Task_from_a_Reminder_while_typing_loads_once_and_leaves_no_search_waiting()
    {
        var s = await StartAsync("Buy milk", "Walk dog");
        s.Vm.SearchText = "milk";
        await SearchRunsOutAsync(s);
        var dog = s.Service.All.Single(t => t.Title == "Walk dog").Id;
        var loads = s.Tags.Loads;

        s.Vm.SearchText = "milk bread";
        await s.Vm.ShowTaskAsync(dog, Ct);

        Assert.False(s.Vm.IsSearchPending, "Opening a Task left a search waiting.");
        s.Clock.Advance(MainViewModel.SearchDelay);
        Assert.Equal(loads + 1, s.Tags.Loads);
        Assert.Equal(dog, s.Vm.DetailTask?.Id);
        Assert.Equal(["milk"], s.Search.Texts);
    }

    [Fact]
    public async Task After_the_stop_typing_queues_no_search()
    {
        var s = await StartAsync("Buy milk");

        await s.Vm.StopAsync();
        s.Vm.SearchText = "milk";

        Assert.False(s.Vm.IsSearchPending, "Typing after the stop left a search waiting.");
        s.Clock.Advance(MainViewModel.SearchDelay);
        Assert.Empty(s.Search.Texts);
        Assert.Null(s.Vm.ApplySearchCommand.ExecutionTask);
    }

    [Fact]
    public async Task The_stop_drops_the_search_waiting_for_typing_to_pause()
    {
        var s = await StartAsync("Buy milk");
        s.Vm.SearchText = "milk";

        await s.Vm.StopAsync();

        Assert.False(s.Vm.IsSearchPending, "A search still waits after the stop.");
        s.Clock.Advance(MainViewModel.SearchDelay);
        Assert.Empty(s.Search.Texts);
        Assert.Null(s.Vm.ApplySearchCommand.ExecutionTask);
    }

    [Fact]
    public async Task A_tick_already_queued_to_the_UI_thread_runs_no_search_after_the_stop()
    {
        var ui = new QueuedContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(ui);
        try
        {
            var s = await StartAsync("Buy milk");
            s.Vm.SearchText = "milk";
            s.Clock.Advance(MainViewModel.SearchDelay);
            Assert.True(ui.HasWork, "The tick was not posted to the UI thread.");

            await s.Vm.StopAsync();
            ui.RunAll();

            Assert.Empty(s.Search.Texts);
            Assert.Null(s.Vm.ApplySearchCommand.ExecutionTask);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    // A UI thread whose posted work runs only when the test says so.
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _work = new();

        public bool HasWork => _work.Count > 0;

        public override void Post(SendOrPostCallback d, object? state) => _work.Enqueue((d, state));

        public void RunAll()
        {
            while (_work.TryDequeue(out var item))
            {
                item.Callback(item.State);
            }
        }
    }

    [Fact]
    public async Task After_the_stop_the_search_command_runs_nothing()
    {
        var s = await StartAsync("Buy milk");
        var loads = s.Tags.Loads;
        await s.Vm.StopAsync();
        s.Vm.SearchText = "milk";

        await s.Vm.ApplySearchCommand.ExecuteAsync(null);

        Assert.Empty(s.Search.Texts);
        Assert.Equal(loads, s.Tags.Loads);
    }
}
