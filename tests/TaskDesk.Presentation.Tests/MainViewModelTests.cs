using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;

namespace TaskDesk.Presentation.Tests;

public class MainViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Load_shows_the_existing_Tasks_in_order()
    {
        var service = new FakeTaskService();
        await service.CreateTaskAsync("one", Ct);
        await service.CreateTaskAsync("two", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service));

        await vm.LoadAsync(Ct);

        Assert.Equal(["one", "two"], vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Quick_add_creates_the_Task_clears_the_box_and_shows_it()
    {
        var service = new FakeTaskService();
        var vm = new MainViewModel(service, new FakeTagService(service)) { QuickAddTitle = "Buy milk" };

        await vm.AddTaskCommand.ExecuteAsync(null);

        Assert.Equal(["Buy milk"], service.Created);
        Assert.Equal(["Buy milk"], vm.Tasks.Select(t => t.Title));
        Assert.Equal("", vm.QuickAddTitle);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Quick_add_rejects_an_empty_title(string title)
    {
        var service = new FakeTaskService();
        var vm = new MainViewModel(service, new FakeTagService(service)) { QuickAddTitle = title };

        Assert.False(vm.AddTaskCommand.CanExecute(null));
        await vm.AddTaskCommand.ExecuteAsync(null);

        Assert.Empty(service.Created);
        Assert.Empty(vm.Tasks);
    }

    [Fact]
    public void Quick_add_command_becomes_executable_once_a_title_is_typed()
    {
        var vm = new MainViewModel(new FakeTaskService(), new FakeTagService(new FakeTaskService()));
        var changes = 0;
        vm.AddTaskCommand.CanExecuteChanged += (_, _) => changes++;

        vm.QuickAddTitle = "x";

        Assert.True(vm.AddTaskCommand.CanExecute(null));
        Assert.True(changes > 0);
    }

    [Fact]
    public async Task Moving_a_Task_reorders_the_list_and_asks_the_service_to_persist_it()
    {
        var service = new FakeTaskService();
        await service.CreateTaskAsync("a", Ct);
        await service.CreateTaskAsync("b", Ct);
        await service.CreateTaskAsync("c", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service));
        await vm.LoadAsync(Ct);
        var a = vm.Tasks[0];

        await vm.MoveTaskAsync(0, 2, Ct);

        Assert.Equal(["b", "c", "a"], vm.Tasks.Select(t => t.Title));
        Assert.Equal([(a.Id, 2)], service.Moves);
    }

    [Fact]
    public async Task Moving_is_ignored_when_reordering_is_disabled()
    {
        var service = new FakeTaskService();
        await service.CreateTaskAsync("a", Ct);
        await service.CreateTaskAsync("b", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service)) { IsReorderEnabled = false };
        await vm.LoadAsync(Ct);

        await vm.MoveTaskAsync(0, 1, Ct);

        Assert.Equal(["a", "b"], vm.Tasks.Select(t => t.Title));
        Assert.Empty(service.Moves);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 1)]
    [InlineData(0, 2)]
    public async Task Moving_to_the_same_or_an_invalid_position_does_nothing(int from, int to)
    {
        var service = new FakeTaskService();
        await service.CreateTaskAsync("a", Ct);
        await service.CreateTaskAsync("b", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service));
        await vm.LoadAsync(Ct);

        await vm.MoveTaskAsync(from, to, Ct);

        Assert.Equal(["a", "b"], vm.Tasks.Select(t => t.Title));
        Assert.Empty(service.Moves);
    }

    [Fact]
    public async Task Adding_a_Tag_to_the_selected_Task_shows_it_on_the_row_and_clears_the_box()
    {
        var service = new FakeTaskService();
        var task = await service.CreateTaskAsync("one", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service));
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks.Single();
        vm.NewTagName = "work";

        await vm.AddTagCommand.ExecuteAsync(null);

        Assert.Equal(["work"], vm.Tasks.Single().Tags.Select(t => t.Name));
        Assert.Equal(["work"], vm.Tags.Select(t => t.Name));
        Assert.Equal("", vm.NewTagName);
        Assert.Equal(task.Id, vm.SelectedTask?.Id);
    }

    [Theory]
    [InlineData("two words")]
    [InlineData("#work")]
    [InlineData("")]
    public async Task Adding_an_invalid_Tag_name_is_not_possible(string name)
    {
        var service = new FakeTaskService();
        await service.CreateTaskAsync("one", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service));
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks.Single();
        vm.NewTagName = name;

        Assert.False(vm.AddTagCommand.CanExecute(null));
    }

    [Fact]
    public async Task Removing_a_Tag_from_the_selected_Task_keeps_the_Tag_in_navigation()
    {
        var service = new FakeTaskService();
        var tags = new FakeTagService(service);
        var task = await service.CreateTaskAsync("one", Ct);
        var tag = await tags.AddTagToTaskAsync(task.Id, "work", Ct);
        var vm = new MainViewModel(service, tags);
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks.Single();

        await vm.RemoveTagCommand.ExecuteAsync(tag);

        Assert.Empty(vm.Tasks.Single().Tags);
        Assert.Single(vm.Tags);
    }

    [Fact]
    public async Task Filtering_by_a_Tag_shows_only_its_Tasks_and_clearing_restores_the_list()
    {
        var service = new FakeTaskService();
        var tags = new FakeTagService(service);
        var tagged = await service.CreateTaskAsync("tagged", Ct);
        await service.CreateTaskAsync("plain", Ct);
        await tags.AddTagToTaskAsync(tagged.Id, "work", Ct);
        var vm = new MainViewModel(service, tags);
        await vm.LoadAsync(Ct);

        await vm.FilterByTagAsync(vm.Tags.Single(), Ct);
        Assert.Equal(["tagged"], vm.Tasks.Select(t => t.Title));

        await vm.FilterByTagAsync(null, Ct);
        Assert.Equal(["tagged", "plain"], vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Deleting_the_filtered_Tag_drops_the_filter_and_shows_all_Tasks_again()
    {
        var service = new FakeTaskService();
        var tags = new FakeTagService(service);
        var tagged = await service.CreateTaskAsync("tagged", Ct);
        await service.CreateTaskAsync("plain", Ct);
        await tags.AddTagToTaskAsync(tagged.Id, "work", Ct);
        var vm = new MainViewModel(service, tags);
        await vm.LoadAsync(Ct);
        await vm.FilterByTagAsync(vm.Tags.Single(), Ct);

        await vm.DeleteTagAsync(vm.Tags.Single(), Ct);

        Assert.Null(vm.TagFilter);
        Assert.Empty(vm.Tags);
        Assert.Equal(2, vm.Tasks.Count);
    }

    private sealed class FakeTagService(FakeTaskService tasks) : ITagService
    {
        private readonly List<Tag> _tags = [];
        private readonly List<(Guid TaskId, Guid TagId)> _links = [];

        public Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Tag>>(_tags.OrderBy(t => t.NormalizedName).ToList());

        public Task<Tag> CreateTagAsync(string name, CancellationToken cancellationToken = default)
        {
            var tag = new Tag { Id = Guid.NewGuid(), Name = TagName.Validate(name), NormalizedName = TagName.Normalize(name) };
            _tags.Add(tag);
            return Task.FromResult(tag);
        }

        public async Task<Tag> AddTagToTaskAsync(Guid taskId, string name, CancellationToken cancellationToken = default)
        {
            TagName.Validate(name);
            var tag = _tags.FirstOrDefault(t => t.NormalizedName == TagName.Normalize(name))
                ?? await CreateTagAsync(name, cancellationToken);
            if (!_links.Contains((taskId, tag.Id)))
            {
                _links.Add((taskId, tag.Id));
            }

            Refresh();
            return tag;
        }

        public Task RemoveTagFromTaskAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default)
        {
            _links.Remove((taskId, tagId));
            Refresh();
            return Task.CompletedTask;
        }

        public Task DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default)
        {
            _tags.RemoveAll(t => t.Id == tagId);
            _links.RemoveAll(l => l.TagId == tagId);
            Refresh();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TaskItem>> ListTasksByTagAsync(Guid tagId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>(
                tasks.All.Where(t => _links.Contains((t.Id, tagId))).ToList());

        private void Refresh()
        {
            foreach (var task in tasks.All)
            {
                task.Tags = _links.Where(l => l.TaskId == task.Id)
                    .Select(l => _tags.Single(t => t.Id == l.TagId)).ToList();
            }
        }
    }

    private sealed class FakeTaskService : ITaskService
    {
        private readonly List<TaskItem> _tasks = [];
        public List<string> Created { get; } = [];
        public IReadOnlyList<TaskItem> All => _tasks;
        public List<(Guid TaskId, int NewIndex)> Moves { get; } = [];

        public Task MoveTaskAsync(Guid taskId, int newIndex, CancellationToken cancellationToken = default)
        {
            Moves.Add((taskId, newIndex));
            var task = _tasks.Single(t => t.Id == taskId);
            _tasks.Remove(task);
            _tasks.Insert(newIndex, task);
            return Task.CompletedTask;
        }

        public Task<TaskItem> CreateTaskAsync(string title, CancellationToken cancellationToken = default)
        {
            var task = new TaskItem { Id = Guid.NewGuid(), Title = TaskTitle.Normalize(title) };
            _tasks.Add(task);
            Created.Add(task.Title);
            return Task.FromResult(task);
        }

        public Task<IReadOnlyList<TaskItem>> ListDefaultListTasksAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>(_tasks.ToList());
    }
}
