using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;

namespace TaskDesk.Views.Tests;

[TestClass]
public sealed class MainWindowTests
{
    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task The_main_window_shows_the_Tasks_of_the_database_as_rows_named_by_their_titles()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var inbox = (await db.QueryAsync(c => c.Lists)).Single().Id;
        await db.AddTaskAsync(inbox, "Book flights", completed: false);
        await db.AddTaskAsync(inbox, "Call the venue", completed: false);
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, listService: db.Get<IListService>());

        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await viewModel.LoadAsync(ct);
            var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
            List<ListViewItem> Rows() => VisualTree.Descendants(list).OfType<ListViewItem>().ToList();
            await UiWait.UntilAsync(() => Rows().Count == 2,
                () => $"Expected 2 rows, found {Rows().Count} ({viewModel.Tasks.Count} Tasks loaded).", ct);
            var rows = Rows();
            CollectionAssert.AreEqual(
                new[] { "Book flights", "Call the venue" },
                rows.Select(r => VisualTree.Descendants(r).OfType<TextBlock>().First().Text).ToList());
            CollectionAssert.AreEqual(
                new[] { "Book flights", "Call the venue" },
                rows.Select(AutomationProperties.GetName).ToList());
            Assert.AreEqual("OpenTaskList", AutomationProperties.GetAutomationId(list));
            Assert.AreEqual("TaskDesk", window.Title);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Quick_add_has_keyboard_focus_when_the_main_window_opens()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, listService: db.Get<IListService>());

        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await viewModel.LoadAsync(ct);

            var quickAdd = VisualTree.Find<TextBox>(window.View, "QuickAddBox");
            await UiWait.UntilAsync(
                () => quickAdd.XamlRoot is { } root && ReferenceEquals(FocusManager.GetFocusedElement(root), quickAdd),
                () => "Quick add did not take keyboard focus.", ct);
            Assert.AreEqual("QuickAddBox", AutomationProperties.GetAutomationId(quickAdd));
            Assert.AreEqual("Quick add", AutomationProperties.GetName(quickAdd));
        }
        finally
        {
            window.Close();
        }
    }
}
