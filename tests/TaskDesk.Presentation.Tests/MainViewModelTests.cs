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
        var vm = new MainViewModel(service);

        await vm.LoadAsync(Ct);

        Assert.Equal(["one", "two"], vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Quick_add_creates_the_Task_clears_the_box_and_shows_it()
    {
        var service = new FakeTaskService();
        var vm = new MainViewModel(service) { QuickAddTitle = "Buy milk" };

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
        var vm = new MainViewModel(service) { QuickAddTitle = title };

        Assert.False(vm.AddTaskCommand.CanExecute(null));
        await vm.AddTaskCommand.ExecuteAsync(null);

        Assert.Empty(service.Created);
        Assert.Empty(vm.Tasks);
    }

    [Fact]
    public void Quick_add_command_becomes_executable_once_a_title_is_typed()
    {
        var vm = new MainViewModel(new FakeTaskService());
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
        var vm = new MainViewModel(service);
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
        var vm = new MainViewModel(service) { IsReorderEnabled = false };
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
        var vm = new MainViewModel(service);
        await vm.LoadAsync(Ct);

        await vm.MoveTaskAsync(from, to, Ct);

        Assert.Equal(["a", "b"], vm.Tasks.Select(t => t.Title));
        Assert.Empty(service.Moves);
    }

    private sealed class FakeTaskService : ITaskService
    {
        private readonly List<TaskItem> _tasks = [];
        public List<string> Created { get; } = [];
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
