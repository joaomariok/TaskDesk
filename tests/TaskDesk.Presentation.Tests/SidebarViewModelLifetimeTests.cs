using TaskDesk.Core.Lists;
using TaskDesk.Core.Tasks;
using FakeDialogs = TaskDesk.Presentation.Tests.SidebarViewModelTests.FakeDialogs;
using FakeListService = TaskDesk.Presentation.Tests.SidebarViewModelTests.FakeListService;

namespace TaskDesk.Presentation.Tests;

/// <summary>The sidebar's Lists stop with their window: after <see cref="SidebarViewModel.StopAsync"/> they do no more work.</summary>
public class SidebarViewModelLifetimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task After_the_stop_a_load_touches_no_service_and_a_second_stop_is_harmless()
    {
        var (vm, service, _) = await StartAsync();

        await vm.StopAsync();
        await vm.StopAsync();
        await vm.LoadAsync(Ct);

        Assert.Equal(0, service.Calls);
        Assert.Equal(["Inbox", "Work"], vm.Lists.Select(l => l.Name));
    }

    public static TheoryData<string> Commands => ["create", "rename", "colour", "move", "delete"];

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task After_the_stop_a_command_touches_no_service_and_asks_nothing(string command)
    {
        var (vm, service, dialogs) = await StartAsync();
        dialogs.PromptResult = "Job";
        dialogs.ConfirmResult = true;
        vm.NewListName = "Home";
        var edited = 0;
        vm.ListEdited += (_, _) => edited++;

        await vm.StopAsync();
        await RunAsync(vm, command);

        Assert.Equal(0, service.Calls);
        Assert.Null(dialogs.ConfirmedFor);
        Assert.Equal(0, edited);
        Assert.Equal(["Inbox", "Work"], vm.Lists.Select(l => l.Name));
    }

    [Fact]
    public async Task A_load_the_stop_cancelled_ends_quietly_when_its_database_is_then_gone()
    {
        var service = new DisposedOnReleaseListService();
        var vm = new SidebarViewModel(SidebarViewModelTests.NewMainViewModel(), service, new FakeDialogs());

        var load = vm.LoadAsync(Ct);
        var stop = vm.StopAsync();
        Assert.False(stop.IsCompleted, "The stop completed while the load still ran.");
        service.Release();

        await load;
        await stop;
        Assert.Empty(vm.Lists);
    }

    [Fact]
    public async Task A_load_whose_database_is_gone_still_fails_when_nothing_stopped_it()
    {
        var service = new DisposedOnReleaseListService();
        var vm = new SidebarViewModel(SidebarViewModelTests.NewMainViewModel(), service, new FakeDialogs());

        var load = vm.LoadAsync(Ct);
        service.Release();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => load);
    }

    [Fact]
    public async Task A_stop_while_the_rename_dialog_is_open_does_not_wait_for_it_and_the_rename_then_does_nothing()
    {
        var fake = new FakeListService();
        await fake.CreateListAsync("Work", Ct);
        var service = new CountingListService(fake);
        var dialogs = new HeldDialogs();
        var vm = new SidebarViewModel(SidebarViewModelTests.NewMainViewModel(), service, dialogs);
        await vm.LoadAsync(Ct);
        service.Reset();

        var rename = vm.RenameCommand.ExecuteAsync(vm.Lists[1]);
        // The exit waits for the stop; an open dialog must not hold it up.
        await vm.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), Ct);
        dialogs.Name.SetResult("Job");
        await rename;

        Assert.Equal(0, service.Calls);
        Assert.Equal(["Inbox", "Work"], vm.Lists.Select(l => l.Name));
    }

    // Holds a load until released, then fails as a disposed database does (its window closed and let it go).
    private sealed class DisposedOnReleaseListService : IListService
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.SetResult();

        public async Task<IReadOnlyList<ListSummary>> GetListsAsync(CancellationToken cancellationToken = default)
        {
            await _release.Task;
            throw new ObjectDisposedException("IServiceProvider");
        }

        public Task<TaskList> CreateListAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RenameListAsync(Guid listId, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetListColorAsync(Guid listId, string? colorHex, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ReorderListsAsync(IReadOnlyList<Guid> orderedListIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteListAsync(Guid listId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    // A rename dialog the owner answers only when the test says so.
    private sealed class HeldDialogs : IListDialogs
    {
        public TaskCompletionSource<string?> Name { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> ConfirmDeleteAsync(string listName, int taskCount) => Task.FromResult(false);

        public Task<string?> PromptNameAsync(string currentName) => Name.Task;
    }

    private static Task RunAsync(SidebarViewModel vm, string command) => command switch
    {
        "create" => vm.CreateListCommand.ExecuteAsync(null),
        "rename" => vm.RenameCommand.ExecuteAsync(vm.Lists[1]),
        "colour" => vm.SetColorAsync(vm.Lists[1], "#336699", Ct),
        "move" => vm.MoveToGapAsync(vm.Lists[1], 0, Ct),
        "delete" => vm.DeleteCommand.ExecuteAsync(vm.Lists[1]),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
    };

    // Counts every call that reaches the List service, and passes it on to the fake.
    private sealed class CountingListService(IListService inner) : IListService
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ListSummary>> GetListsAsync(CancellationToken cancellationToken = default)
            => Count(inner.GetListsAsync(cancellationToken));

        public Task<TaskList> CreateListAsync(string name, CancellationToken cancellationToken = default)
            => Count(inner.CreateListAsync(name, cancellationToken));

        public Task RenameListAsync(Guid listId, string name, CancellationToken cancellationToken = default)
            => Count(inner.RenameListAsync(listId, name, cancellationToken));

        public Task SetListColorAsync(Guid listId, string? colorHex, CancellationToken cancellationToken = default)
            => Count(inner.SetListColorAsync(listId, colorHex, cancellationToken));

        public Task ReorderListsAsync(IReadOnlyList<Guid> orderedListIds, CancellationToken cancellationToken = default)
            => Count(inner.ReorderListsAsync(orderedListIds, cancellationToken));

        public Task DeleteListAsync(Guid listId, CancellationToken cancellationToken = default)
            => Count(inner.DeleteListAsync(listId, cancellationToken));

        public void Reset() => Calls = 0;

        private T Count<T>(T call)
        {
            Calls++;
            return call;
        }
    }

    // Loaded with the Inbox and "Work"; the calls the start made are not counted.
    private static async Task<(SidebarViewModel Vm, CountingListService Service, FakeDialogs Dialogs)> StartAsync()
    {
        var fake = new FakeListService();
        await fake.CreateListAsync("Work", Ct);
        var service = new CountingListService(fake);
        var dialogs = new FakeDialogs();
        var vm = new SidebarViewModel(SidebarViewModelTests.NewMainViewModel(), service, dialogs);
        await vm.LoadAsync(Ct);
        service.Reset();
        return (vm, service, dialogs);
    }
}
