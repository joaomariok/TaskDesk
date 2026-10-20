using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The sidebar's four selectors (the Smart Views, the Lists, the Tags and the rail's Tags) show the page's entry
/// after each way of getting there: a click, a keyboard selection, a Reminder opening a Task, and a deleted List.
/// </summary>
[TestClass]
public sealed class SidebarSelectionTests
{
    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task A_click_on_a_List_moves_the_selection_from_a_Smart_View_to_that_List()
    {
        using var db = await DatabaseAsync();
        var (window, viewModel, sidebar) = NewWindow(db);
        try
        {
            await OpenAsync(window, viewModel, sidebar);
            await viewModel.ShowSmartViewAsync(SmartView.Today, Ct);
            Assert.AreEqual("Today | - | - | -", Selection(window));

            Click(await WorkRowAsync(window));

            await UiWait.UntilAsync(() => Selection(window) == "- | Work | - | -",
                () => $"The selectors show {Selection(window)}.", Ct);
            Assert.AreEqual("Work", viewModel.PageHeading);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_keyboard_selection_of_a_Tag_moves_the_selection_from_a_List_to_that_Tag()
    {
        using var db = await DatabaseAsync();
        var (window, viewModel, sidebar) = NewWindow(db);
        try
        {
            await OpenAsync(window, viewModel, sidebar);
            Click(await WorkRowAsync(window));
            await UiWait.UntilAsync(() => Selection(window) == "- | Work | - | -",
                () => $"The selectors show {Selection(window)}.", Ct);

            // What an arrow key in the Tags does: it moves the selection to the next row.
            ((ListView)WithId(window.Content, "TagList")!).SelectedIndex = 0;

            await UiWait.UntilAsync(() => Selection(window) == "- | - | home | home",
                () => $"The selectors show {Selection(window)}.", Ct);
            Assert.AreEqual("home", viewModel.TagFilter?.Name);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Opening_a_Task_from_a_Reminder_moves_the_selection_from_a_List_to_All()
    {
        using var db = await DatabaseAsync();
        var (window, viewModel, sidebar) = NewWindow(db);
        try
        {
            await OpenAsync(window, viewModel, sidebar);
            Click(await WorkRowAsync(window));
            await UiWait.UntilAsync(() => Selection(window) == "- | Work | - | -",
                () => $"The selectors show {Selection(window)}.", Ct);
            var payRent = (await db.QueryAsync(c => c.Tasks)).Single(t => t.Title == "Pay rent").Id;

            // What a click on the Task's Reminder does; the Task is not on Work's page.
            await viewModel.ShowTaskAsync(payRent, Ct);

            Assert.AreEqual("All | - | - | -", Selection(window));
            Assert.AreEqual(payRent, viewModel.DetailTask?.Id);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Deleting_the_List_shown_moves_the_selection_to_the_Default_List()
    {
        using var db = await DatabaseAsync();
        var (window, viewModel, sidebar) = NewWindow(db);
        try
        {
            await OpenAsync(window, viewModel, sidebar);
            Click(await WorkRowAsync(window));
            await UiWait.UntilAsync(() => Selection(window) == "- | Work | - | -",
                () => $"The selectors show {Selection(window)}.", Ct);

            await sidebar.DeleteCommand.ExecuteAsync(sidebar.Lists.Single(l => l.Name == "Work"));

            await UiWait.UntilAsync(() => Selection(window) == "- | Inbox | - | -" && viewModel.PageHeading == "Inbox",
                () => $"The selectors show {Selection(window)} on {viewModel.PageHeading}.", Ct);
        }
        finally
        {
            window.Close();
        }
    }

    private CancellationToken Ct => TestContext.CancellationToken;

    // "Inbox" with "Pay rent" tagged #home, and "Work" with "Write report".
    private async Task<TempDatabase> DatabaseAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        var inbox = (await db.QueryAsync(c => c.Lists)).Single().Id;
        var work = await db.Get<IListService>().CreateListAsync("Work", Ct);
        await db.AddTaskAsync(inbox, "Pay rent", completed: false);
        await db.AddTaskAsync(work.Id, "Write report", completed: false);
        var payRent = (await db.QueryAsync(c => c.Tasks)).Single(t => t.Title == "Pay rent").Id;
        await db.Get<ITagService>().AddTagToTaskAsync(payRent, "home", Ct);
        return db;
    }

    private static (MainWindow Window, MainViewModel ViewModel, SidebarViewModel Sidebar) NewWindow(TempDatabase db)
    {
        var viewModel = new MainViewModel(db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, db.Get<IDueService>(),
            db.Get<ITaskSearchService>(), listService: db.Get<IListService>(), taskCreator: db.Get<ITaskCreator>());
        var sidebar = new SidebarViewModel(viewModel, db.Get<IListService>(), new ConfirmingDialogs());
        return (new MainWindow(viewModel, sidebar), viewModel, sidebar);
    }

    private async Task OpenAsync(MainWindow window, MainViewModel viewModel, SidebarViewModel sidebar)
    {
        window.Activate();
        await viewModel.LoadAsync(Ct);
        await sidebar.LoadAsync(Ct);
        await UiWait.UntilAsync(() => Selection(window) == "- | Inbox | - | -",
            () => $"The first page's selectors show {Selection(window)}.", Ct);
    }

    private async Task<ListViewItem> WorkRowAsync(MainWindow window)
    {
        var list = (ListView)WithId(window.Content, "ListsList")!;
        ListViewItem? Row() => Enumerable.Range(0, list.Items.Count).Select(list.ContainerFromIndex).OfType<ListViewItem>()
            .FirstOrDefault(r => r.IsLoaded && AutomationProperties.GetName(r) == "Work");
        await UiWait.UntilAsync(() => Row() is not null, () => "Work's row did not show.", Ct);
        return Row()!;
    }

    // "Smart View | List | Tag | rail's Tag", with "-" for a selector that selects nothing.
    private static string Selection(MainWindow window)
    {
        var root = window.Content;
        var view = (window.Navigation.Pane.SelectedItem as NavigationViewItem)?.Content as string;
        var list = (((ListView?)WithId(root, "ListsList"))?.SelectedItem as ListItemViewModel)?.Name;
        var tag = (((ListView?)WithId(root, "TagList"))?.SelectedItem as Tag)?.Name;
        var railTag = ((((Button?)WithId(root, "RailTagsButton"))?.Flyout as Flyout)?.Content as ListView)?.SelectedItem as Tag;
        return string.Join(" | ", new[] { view, list, tag, railTag?.Name }.Select(s => s ?? "-"));
    }

    private static FrameworkElement? WithId(UIElement root, string id) =>
        VisualTree.Descendants(root).OfType<FrameworkElement>().FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == id);

    private static void Click(ListViewItem row) =>
        ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(row).GetPattern(PatternInterface.Invoke)
            ?? throw new AssertFailedException("The List row cannot be invoked.")).Invoke();

    // Confirms every List delete.
    private sealed class ConfirmingDialogs : IListDialogs
    {
        public Task<bool> ConfirmDeleteAsync(string listName, int taskCount) => Task.FromResult(true);

        public Task<string?> PromptNameAsync(string currentName) => Task.FromResult<string?>(null);
    }
}
