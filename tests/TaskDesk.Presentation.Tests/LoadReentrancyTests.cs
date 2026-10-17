using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

// Overlapping LoadAsync calls (a reload from a Task change while a search reload runs, say).
public sealed class LoadReentrancyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Holds every ListTagsAsync call until released, in the order they were made.
    private sealed class GatedTagService(FakeTagService inner) : ITagService
    {
        private readonly List<TaskCompletionSource> _gates = [];

        public bool Hold { get; set; }

        public void Release(int index) => _gates[index].SetResult();

        public async Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken cancellationToken = default)
        {
            if (Hold)
            {
                var gate = new TaskCompletionSource();
                _gates.Add(gate);
                await gate.Task;
            }

            return await inner.ListTagsAsync(cancellationToken);
        }

        public Task<Tag> CreateTagAsync(string name, CancellationToken cancellationToken = default) =>
            inner.CreateTagAsync(name, cancellationToken);

        public Task<Tag> AddTagToTaskAsync(Guid taskId, string name, CancellationToken cancellationToken = default) =>
            inner.AddTagToTaskAsync(taskId, name, cancellationToken);

        public Task RemoveTagFromTaskAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default) =>
            inner.RemoveTagFromTaskAsync(taskId, tagId, cancellationToken);

        public Task DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default) =>
            inner.DeleteTagAsync(tagId, cancellationToken);

        public Task<IReadOnlyList<TaskItem>> ListTasksByTagAsync(Guid tagId, CancellationToken cancellationToken = default) =>
            inner.ListTasksByTagAsync(tagId, cancellationToken);
    }

    // Holds every search until released, so a test decides which query answers first.
    private sealed class GatedSearchService(FakeTaskService tasks) : ITaskSearchService
    {
        private readonly List<(string Text, TaskCompletionSource Gate)> _pending = [];

        public void Release(string text) => _pending.Single(p => p.Text == text).Gate.SetResult();

        public async Task<IReadOnlyList<TaskItem>> SearchAsync(
            TaskSearchQuery query, CancellationToken cancellationToken = default)
        {
            var text = query.Text ?? "";
            var gate = new TaskCompletionSource();
            _pending.Add((text, gate));
            await gate.Task;
            return [.. tasks.All.Where(t => t.Title.Contains(text, StringComparison.OrdinalIgnoreCase))];
        }
    }

    [Fact]
    public async Task Overlapping_loads_do_not_duplicate_the_Tags()
    {
        var tasks = new FakeTaskService();
        var a = await tasks.CreateTaskAsync("a", Ct);
        var inner = new FakeTagService(tasks);
        await inner.AddTagToTaskAsync(a.Id, "work", Ct);
        await inner.AddTagToTaskAsync(a.Id, "home", Ct);
        var tags = new GatedTagService(inner);
        var vm = new MainViewModel(tasks, tags);
        await vm.LoadAsync(Ct);

        tags.Hold = true;
        var first = vm.LoadAsync(Ct);
        var second = vm.LoadAsync(Ct);
        tags.Release(0);
        tags.Release(1);
        await Task.WhenAll(first, second);

        Assert.Equal(["home", "work"], vm.Tags.Select(t => t.Name));
        Assert.Equal(["home", "work"], vm.FilterTagChips.Select(c => c.Tag.Name));
        Assert.Equal(["a"], vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task The_latest_search_wins_when_an_older_one_answers_last()
    {
        var tasks = new FakeTaskService();
        await tasks.CreateTaskAsync("milk", Ct);
        await tasks.CreateTaskAsync("bread", Ct);
        var search = new GatedSearchService(tasks);
        var vm = new MainViewModel(tasks, new FakeTagService(tasks), searchService: search);
        await vm.LoadAsync(Ct);

        vm.SearchText = "milk";
        var older = vm.LoadAsync(Ct);
        vm.SearchText = "bread";
        var latest = vm.LoadAsync(Ct);

        search.Release("bread");
        search.Release("milk");
        await Task.WhenAll(older, latest);

        Assert.Equal(["bread"], vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Overlapping_loads_keep_the_selected_Task_selected()
    {
        var tasks = new FakeTaskService();
        await tasks.CreateTaskAsync("a", Ct);
        await tasks.CreateTaskAsync("b", Ct);
        var tags = new GatedTagService(new FakeTagService(tasks));
        var vm = new MainViewModel(tasks, tags);
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks[1];

        tags.Hold = true;
        var first = vm.LoadAsync(Ct);
        var second = vm.LoadAsync(Ct);
        tags.Release(0);
        tags.Release(1);
        await Task.WhenAll(first, second);

        Assert.Equal("b", vm.SelectedTask?.Title);
    }
}
