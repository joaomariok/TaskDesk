using TaskDesk.Core.Lists;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation.Tests;

public sealed class QuickAddViewModelTests
{
    private sealed class FakeCreator : ITaskCreator
    {
        public List<(string Title, Guid ListId)> Created { get; } = [];

        public List<DateOnly?> CreatedDue { get; } = [];

        public Task<TaskItem> CreateTaskInListAsync(string title, Guid listId, CancellationToken cancellationToken = default)
            => CreateTaskInListAsync(title, listId, null, cancellationToken);

        public Task<TaskItem> CreateTaskInListAsync(
            string title, Guid listId, DateOnly? dueDate, CancellationToken cancellationToken = default)
        {
            Created.Add((title, listId));
            CreatedDue.Add(dueDate);
            return Task.FromResult(new TaskItem { Id = Guid.NewGuid(), ListId = listId, Title = title, DueDate = dueDate });
        }
    }

    private sealed class FakeLists(IReadOnlyList<TaskList> lists) : IListService
    {
        public Task<IReadOnlyList<ListSummary>> GetListsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ListSummary>>([.. lists.Select(l => new ListSummary(l, 0))]);

        public Task<TaskList> CreateListAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RenameListAsync(Guid listId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetListColorAsync(Guid listId, string? colorHex, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReorderListsAsync(IReadOnlyList<Guid> orderedListIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteListAsync(Guid listId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private readonly TaskList _work = new() { Id = Guid.NewGuid(), Name = "Work", SortOrder = 0 };
    private readonly TaskList _inbox = new() { Id = Guid.NewGuid(), Name = "Inbox", SortOrder = 1, IsDefault = true };
    private readonly FakeCreator _creator = new();

    private async Task<QuickAddViewModel> LoadedAsync()
    {
        var vm = new QuickAddViewModel(_creator, new FakeLists([_work, _inbox]));
        await vm.LoadAsync(TestContext.Current.CancellationToken);
        return vm;
    }

    [Fact]
    public async Task Default_List_is_preselected()
    {
        var vm = await LoadedAsync();

        Assert.Equal(2, vm.Lists.Count);
        Assert.Same(_inbox, vm.SelectedList);
    }

    [Fact]
    public async Task Save_creates_the_Task_in_the_selected_List_and_closes()
    {
        var vm = await LoadedAsync();
        var created = 0;
        var closed = 0;
        vm.TaskCreated += () => created++;
        vm.CloseRequested += () => closed++;
        vm.Title = "Call Ana";
        vm.SelectedList = _work;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal([("Call Ana", _work.Id)], _creator.Created);
        Assert.Equal(1, created);
        Assert.Equal(1, closed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Save_with_an_empty_title_creates_nothing_and_stays_open(string title)
    {
        var vm = await LoadedAsync();
        var closed = 0;
        vm.CloseRequested += () => closed++;
        vm.Title = title;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_creator.Created);
        Assert.Equal(0, closed);
    }

    [Fact]
    public async Task Cancel_closes_without_saving()
    {
        var vm = await LoadedAsync();
        var closed = 0;
        vm.CloseRequested += () => closed++;
        vm.Title = "never saved";

        vm.CancelCommand.Execute(null);

        Assert.Empty(_creator.Created);
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task Due_date_is_optional_and_does_not_block_saving()
    {
        var vm = await LoadedAsync();
        vm.Title = "with date";
        vm.DueDate = new DateTime(2026, 11, 1);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Single(_creator.Created);
    }

    [Fact]
    public async Task Save_passes_the_picked_Due_date_to_the_creator_as_a_calendar_date()
    {
        var vm = await LoadedAsync();
        vm.Title = "with date";
        vm.DueDate = new DateTime(2026, 11, 1, 15, 45, 0);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal([new DateOnly(2026, 11, 1)], _creator.CreatedDue);
    }

    [Fact]
    public async Task Save_without_a_Due_date_passes_none()
    {
        var vm = await LoadedAsync();
        vm.Title = "no date";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal([(DateOnly?)null], _creator.CreatedDue);
    }
}
