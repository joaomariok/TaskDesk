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

    private sealed class FakeTaskService : ITaskService
    {
        private readonly List<TaskItem> _tasks = [];
        public List<string> Created { get; } = [];

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
