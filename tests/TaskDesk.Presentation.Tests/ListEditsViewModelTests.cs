using FakeDialogs = TaskDesk.Presentation.Tests.ListsViewModelTests.FakeDialogs;
using FakeListService = TaskDesk.Presentation.Tests.ListsViewModelTests.FakeListService;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

// A List renamed or recoloured from the heading menu or the sidebar shows its new name and colour on the
// page and on its Tasks' rows.
public class ListEditsViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Setup(
        FakeListService ListService, FakeDialogs Dialogs, ListsViewModel Lists, MainViewModel Vm);

    // One Task in the Default List ("Inbox"); the main view model shows that List.
    private static async Task<Setup> StartAsync()
    {
        var listService = new FakeListService();
        var inbox = (await listService.GetListsAsync(Ct)).Single().List;
        var tasks = new FakeTaskService();
        var task = await tasks.CreateTaskAsync("one", Ct);
        task.ListId = inbox.Id;

        var dialogs = new FakeDialogs();
        var lists = new ListsViewModel(listService, dialogs);
        await lists.LoadAsync(Ct);
        var vm = new MainViewModel(tasks, new FakeTagService(tasks), listService: listService);
        await vm.LoadAsync(Ct);
        return new Setup(listService, dialogs, lists, vm);
    }

    [Fact]
    public async Task Loading_names_the_page_after_the_Default_List()
    {
        var s = await StartAsync();
        await s.ListService.RenameListAsync(s.Lists.Lists[0].Id, "Home", Ct);

        await s.Vm.LoadAsync(Ct);

        Assert.Equal("Home", s.Vm.DefaultListName);
        Assert.Equal("Home", s.Vm.PageHeading);
        Assert.Equal("Add a task to Home", s.Vm.QuickAddTarget);
    }

    [Fact]
    public async Task Without_a_List_service_the_Default_List_name_is_kept()
    {
        var tasks = new FakeTaskService();
        var vm = new MainViewModel(tasks, new FakeTagService(tasks)) { DefaultListName = "Work" };

        await vm.LoadAsync(Ct);

        Assert.Equal("Work", vm.DefaultListName);
    }

    [Fact]
    public async Task Renaming_a_List_announces_the_edit()
    {
        var s = await StartAsync();
        var edits = 0;
        s.Lists.ListEdited += (_, _) => edits++;
        s.Dialogs.PromptResult = "Home";

        await s.Lists.RenameCommand.ExecuteAsync(s.Lists.Lists[0]);

        Assert.Equal(1, edits);
    }

    [Fact]
    public async Task Cancelling_a_rename_announces_nothing()
    {
        var s = await StartAsync();
        var edits = 0;
        s.Lists.ListEdited += (_, _) => edits++;
        s.Dialogs.PromptResult = null;

        await s.Lists.RenameCommand.ExecuteAsync(s.Lists.Lists[0]);

        Assert.Equal(0, edits);
    }

    [Fact]
    public async Task Recolouring_a_List_announces_the_edit()
    {
        var s = await StartAsync();
        var edits = 0;
        s.Lists.ListEdited += (_, _) => edits++;

        await s.Lists.SetColorAsync(s.Lists.Lists[0], "#C42B1C", Ct);

        Assert.Equal(1, edits);
    }

    [Fact]
    public async Task Reloading_after_a_List_edit_refreshes_the_rows_of_its_Tasks()
    {
        var s = await StartAsync();
        Assert.False(s.Vm.Tasks.Single().HasListColor);
        s.Dialogs.PromptResult = "Home";
        await s.Lists.RenameCommand.ExecuteAsync(s.Lists.Lists[0]);
        await s.Lists.SetColorAsync(s.Lists.Lists[0], "#C42B1C", Ct);

        await s.Vm.LoadAsync(Ct); // what the window does on ListEdited

        var row = s.Vm.Tasks.Single();
        Assert.Equal("Home", row.ListName);
        Assert.Equal("#C42B1C", row.ListColorHex);
        Assert.Equal("Home", s.Vm.PageHeading);
    }
}
