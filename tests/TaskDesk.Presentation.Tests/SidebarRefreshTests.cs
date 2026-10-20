using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tasks;
using FakeDialogs = TaskDesk.Presentation.Tests.SidebarViewModelTests.FakeDialogs;
using FakeListService = TaskDesk.Presentation.Tests.SidebarViewModelTests.FakeListService;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

// The sidebar keeps the page and the Lists in step: one refresh reloads both, a counts change reloads the Lists, and a
// List edit reloads the page.
public class SidebarRefreshTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Setup(
        FakeListService ListService, FakeDialogs Dialogs, List<string> Reads, MainViewModel Main, SidebarViewModel Sidebar);

    // "Inbox" (the Default List) and "Work"; both view models loaded on the Default List's page. Reads records which
    // view model read the Lists, from the start on: "page" for the main view model, "lists" for the sidebar.
    private static async Task<Setup> StartAsync()
    {
        var listService = new FakeListService();
        await listService.CreateListAsync("Work", Ct);
        var reads = new List<string>();
        var tasks = new FakeTaskService();
        var dialogs = new FakeDialogs();
        var main = new MainViewModel(
            tasks, new FakeTagService(tasks), new FakeTimeProvider(),
            listService: new RecordingListService(listService, reads, "page"));
        var sidebar = new SidebarViewModel(main, new RecordingListService(listService, reads, "lists"), dialogs);
        await main.LoadAsync(Ct);
        await sidebar.LoadAsync(Ct);
        reads.Clear();
        return new Setup(listService, dialogs, reads, main, sidebar);
    }

    [Fact]
    public async Task Refresh_reloads_the_page_then_the_Lists()
    {
        var s = await StartAsync();
        await s.ListService.RenameListAsync(s.Sidebar.Lists[0].Id, "Home", Ct);

        await s.Sidebar.RefreshAsync(Ct);

        Assert.Equal(["page", "lists"], s.Reads);
        Assert.Equal("Home", s.Main.PageHeading);
        Assert.Equal("Home", s.Sidebar.Lists[0].Name);
    }

    [Fact]
    public async Task A_counts_change_reloads_the_Lists()
    {
        var s = await StartAsync();
        s.ListService.OpenCounts["Inbox"] = 1;
        s.Main.QuickAddTitle = "Buy milk";

        await s.Main.AddTaskCommand.ExecuteAsync(null);

        Assert.Equal(["lists"], s.Reads);
        Assert.Equal(1, s.Sidebar.Lists[0].OpenTaskCount);
    }

    [Fact]
    public async Task Renaming_a_List_reloads_the_page()
    {
        var s = await StartAsync();
        s.Dialogs.PromptResult = "Home";

        await s.Sidebar.RenameCommand.ExecuteAsync(s.Sidebar.Lists[0]);

        Assert.Equal(["lists", "page"], s.Reads);
        Assert.Equal("Home", s.Main.PageHeading);
    }

    [Fact]
    public async Task Recolouring_a_List_reloads_the_page()
    {
        var s = await StartAsync();

        await s.Sidebar.SetColorAsync(s.Sidebar.Lists[0], "#C42B1C", Ct);

        Assert.Equal(["lists", "page"], s.Reads);
    }

    [Fact]
    public async Task Deleting_the_List_shown_reloads_the_page_onto_the_Default_List()
    {
        var s = await StartAsync();
        var work = s.Sidebar.Lists[1];
        await s.Sidebar.OpenAsync(new SidebarEntry.ListEntry(work.Id), Ct);
        s.Reads.Clear();
        s.Dialogs.ConfirmResult = true;

        await s.Sidebar.DeleteCommand.ExecuteAsync(work);

        Assert.Equal(["lists", "page"], s.Reads);
        Assert.Equal("Inbox", s.Main.PageHeading);
        Assert.True(s.Sidebar.SelectedList?.IsDefault);
    }

    [Fact]
    public async Task A_refresh_after_the_stop_does_nothing()
    {
        var s = await StartAsync();

        await s.Sidebar.StopAsync();
        await s.Sidebar.RefreshAsync(Ct);

        Assert.Empty(s.Reads);
    }

    [Fact]
    public async Task A_counts_change_after_the_stop_reloads_nothing()
    {
        var s = await StartAsync();
        s.Main.QuickAddTitle = "Buy milk";

        await s.Sidebar.StopAsync();
        await s.Main.AddTaskCommand.ExecuteAsync(null);

        Assert.Empty(s.Reads);
    }

    // Records each read of the Lists under a name, so a test sees which view model read and in what order.
    private sealed class RecordingListService(IListService inner, List<string> reads, string name) : IListService
    {
        public Task<IReadOnlyList<ListSummary>> GetListsAsync(CancellationToken cancellationToken = default)
        {
            reads.Add(name);
            return inner.GetListsAsync(cancellationToken);
        }

        public Task<TaskList> CreateListAsync(string listName, CancellationToken cancellationToken = default)
            => inner.CreateListAsync(listName, cancellationToken);

        public Task RenameListAsync(Guid listId, string listName, CancellationToken cancellationToken = default)
            => inner.RenameListAsync(listId, listName, cancellationToken);

        public Task SetListColorAsync(Guid listId, string? colorHex, CancellationToken cancellationToken = default)
            => inner.SetListColorAsync(listId, colorHex, cancellationToken);

        public Task ReorderListsAsync(IReadOnlyList<Guid> orderedListIds, CancellationToken cancellationToken = default)
            => inner.ReorderListsAsync(orderedListIds, cancellationToken);

        public Task DeleteListAsync(Guid listId, CancellationToken cancellationToken = default)
            => inner.DeleteListAsync(listId, cancellationToken);
    }
}
