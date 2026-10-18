using TaskDesk.Core.Lists;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation.Tests;

public class ListsViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Load_shows_the_Lists_in_order_with_open_counts()
    {
        var (vm, _, _) = await StartAsync(("Work", 3));

        Assert.Equal(["Inbox", "Work"], vm.Lists.Select(l => l.Name));
        Assert.Equal([0, 3], vm.Lists.Select(l => l.OpenTaskCount));
    }

    [Fact]
    public async Task Creating_a_List_clears_the_box_and_shows_it()
    {
        var (vm, _, _) = await StartAsync();
        vm.NewListName = "Home";

        await vm.CreateListCommand.ExecuteAsync(null);

        Assert.Equal("", vm.NewListName);
        Assert.Equal(["Inbox", "Home"], vm.Lists.Select(l => l.Name));
    }

    [Fact]
    public async Task Creating_a_List_with_an_empty_name_is_not_possible()
    {
        var (vm, _, _) = await StartAsync();
        vm.NewListName = "  ";

        Assert.False(vm.CreateListCommand.CanExecute(null));
        await vm.CreateListCommand.ExecuteAsync(null);

        Assert.Single(vm.Lists);
    }

    [Fact]
    public async Task A_duplicate_name_shows_an_error_and_changes_nothing()
    {
        var (vm, _, _) = await StartAsync(("Work", 0));
        vm.NewListName = "work";

        await vm.CreateListCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Equal(2, vm.Lists.Count);
    }

    [Fact]
    public async Task Rename_uses_the_name_from_the_dialog()
    {
        var (vm, _, dialogs) = await StartAsync(("Work", 0));
        dialogs.PromptResult = "Job";

        await vm.RenameCommand.ExecuteAsync(vm.Lists[1]);

        Assert.Equal(["Inbox", "Job"], vm.Lists.Select(l => l.Name));
    }

    [Fact]
    public async Task Cancelling_the_rename_dialog_changes_nothing()
    {
        var (vm, _, dialogs) = await StartAsync(("Work", 0));
        dialogs.PromptResult = null;

        await vm.RenameCommand.ExecuteAsync(vm.Lists[1]);

        Assert.Equal(["Inbox", "Work"], vm.Lists.Select(l => l.Name));
    }

    [Fact]
    public async Task Colour_is_set_and_cleared()
    {
        var (vm, _, _) = await StartAsync(("Work", 0));

        await vm.SetColorAsync(vm.Lists[1], "#336699", Ct);
        Assert.Equal("#336699", vm.Lists[1].ColorHex);

        await vm.SetColorAsync(vm.Lists[1], "", Ct);
        Assert.Null(vm.Lists[1].ColorHex);
    }

    [Fact]
    public async Task Delete_asks_for_confirmation_and_removes_the_List_when_confirmed()
    {
        var (vm, _, dialogs) = await StartAsync(("Work", 2));
        dialogs.ConfirmResult = true;

        await vm.DeleteCommand.ExecuteAsync(vm.Lists[1]);

        Assert.Equal(("Work", 2), dialogs.ConfirmedFor);
        Assert.Equal(["Inbox"], vm.Lists.Select(l => l.Name));
    }

    [Fact]
    public async Task ListOrDefault_finds_the_List_or_falls_back_to_the_Default_List()
    {
        var (vm, _, _) = await StartAsync(("Work", 0));

        Assert.Equal("Work", vm.ListOrDefault(vm.Lists[1].Id)?.Name);
        Assert.Equal("Inbox", vm.ListOrDefault(Guid.NewGuid())?.Name);
        Assert.Equal("Inbox", vm.ListOrDefault(null)?.Name);
    }

    [Fact]
    public async Task Delete_does_nothing_when_declined()
    {
        var (vm, _, dialogs) = await StartAsync(("Work", 2));
        dialogs.ConfirmResult = false;

        await vm.DeleteCommand.ExecuteAsync(vm.Lists[1]);

        Assert.Equal(2, vm.Lists.Count);
    }

    [Fact]
    public async Task The_Default_List_is_never_deleted_nor_even_asked_about()
    {
        var (vm, service, dialogs) = await StartAsync();
        dialogs.ConfirmResult = true;

        await vm.DeleteCommand.ExecuteAsync(vm.Lists[0]);

        Assert.Null(dialogs.ConfirmedFor);
        Assert.Empty(service.Deleted);
        Assert.Single(vm.Lists);
    }

    [Fact]
    public async Task Moving_a_List_onto_another_reorders_them()
    {
        var (vm, _, _) = await StartAsync(("A", 0), ("B", 0));

        await vm.MoveAsync(vm.Lists[2], vm.Lists[0], Ct);
        Assert.Equal(["B", "Inbox", "A"], vm.Lists.Select(l => l.Name));

        await vm.MoveAsync(vm.Lists[0], vm.Lists[2], Ct);
        Assert.Equal(["Inbox", "A", "B"], vm.Lists.Select(l => l.Name));
    }

    private static async Task<(ListsViewModel Vm, FakeListService Service, FakeDialogs Dialogs)> StartAsync(
        params (string Name, int Open)[] extra)
    {
        var service = new FakeListService();
        foreach (var (name, open) in extra)
        {
            await service.CreateListAsync(name, Ct);
            service.OpenCounts[name] = open;
        }

        var dialogs = new FakeDialogs();
        var vm = new ListsViewModel(service, dialogs);
        await vm.LoadAsync(Ct);
        return (vm, service, dialogs);
    }

    internal sealed class FakeDialogs : IListDialogs
    {
        public bool ConfirmResult { get; set; }
        public string? PromptResult { get; set; }
        public (string Name, int Count)? ConfirmedFor { get; private set; }

        public Task<bool> ConfirmDeleteAsync(string listName, int taskCount)
        {
            ConfirmedFor = (listName, taskCount);
            return Task.FromResult(ConfirmResult);
        }

        public Task<string?> PromptNameAsync(string currentName) => Task.FromResult(PromptResult);
    }

    internal sealed class FakeListService : IListService
    {
        private readonly List<TaskList> _lists =
            [new() { Id = Guid.NewGuid(), Name = "Inbox", IsDefault = true }];

        public Dictionary<string, int> OpenCounts { get; } = [];
        public List<Guid> Deleted { get; } = [];

        public Task<IReadOnlyList<ListSummary>> GetListsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ListSummary>>(
                _lists.Select(l => new ListSummary(Copy(l), OpenCounts.GetValueOrDefault(l.Name))).ToList());

        public Task<TaskList> CreateListAsync(string name, CancellationToken cancellationToken = default)
        {
            var normalized = ListName.Normalize(name);
            if (_lists.Any(l => string.Equals(l.Name, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DuplicateListNameException(normalized);
            }

            var list = new TaskList { Id = Guid.NewGuid(), Name = normalized };
            _lists.Add(list);
            return Task.FromResult(list);
        }

        public Task RenameListAsync(Guid listId, string name, CancellationToken cancellationToken = default)
        {
            var list = _lists.Single(l => l.Id == listId);
            OpenCounts[ListName.Normalize(name)] = OpenCounts.GetValueOrDefault(list.Name);
            list.Name = ListName.Normalize(name);
            return Task.CompletedTask;
        }

        public Task SetListColorAsync(Guid listId, string? colorHex, CancellationToken cancellationToken = default)
        {
            _lists.Single(l => l.Id == listId).ColorHex = ListColor.Normalize(colorHex);
            return Task.CompletedTask;
        }

        public Task ReorderListsAsync(IReadOnlyList<Guid> orderedListIds, CancellationToken cancellationToken = default)
        {
            var byId = _lists.ToDictionary(l => l.Id);
            _lists.Clear();
            _lists.AddRange(orderedListIds.Select(id => byId[id]));
            return Task.CompletedTask;
        }

        public Task DeleteListAsync(Guid listId, CancellationToken cancellationToken = default)
        {
            Deleted.Add(listId);
            _lists.RemoveAll(l => l.Id == listId);
            return Task.CompletedTask;
        }

        private static TaskList Copy(TaskList l) => new()
        {
            Id = l.Id, Name = l.Name, ColorHex = l.ColorHex, IsDefault = l.IsDefault,
        };
    }
}
