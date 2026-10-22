using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using Windows.Foundation;

namespace TaskDesk.Views.Tests;

/// <summary>
/// What interrupts the Task list's own drag (ADR 0007): the window losing focus, the page changing or reloading,
/// and the dragged Task leaving the list each cancel it at once (picture and line gone, nothing moved), while a
/// change that leaves the dragged Task in the list does not. Each starts a drag on the real list, then interrupts it.
/// Lost pointer capture needs a real pointer and is a manual check.
/// </summary>
[TestClass]
public sealed class TaskDragInterruptionTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task The_window_losing_focus_cancels_the_drag_and_moves_nothing()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "A");
        await AddAsync(db, "B");
        var (window, viewModel) = Open(db);
        var other = new Window { Title = "Another window" };
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await WaitForRowsAsync(window, "A", "B");
            var drag = window.View.TaskList.TaskDrag;
            var at = await StartDragAsync(window, list, "A", Bottom(Container(list, "B"), list) - 3);

            other.Activate();

            await UiWait.UntilAsync(() => !drag.IsDragging, () => "Losing focus did not cancel the drag.", Ct);
            AssertCancelled(window, at);
            CollectionAssert.AreEqual(new[] { "A", "B" }, await StoredOrderAsync(db));
        }
        finally
        {
            other.Close();
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Changing_the_page_cancels_the_drag_at_once()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "A");
        await AddAsync(db, "B");
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await WaitForRowsAsync(window, "A", "B");
            var at = await StartDragAsync(window, list, "A", Bottom(Container(list, "B"), list) - 3);

            var showing = viewModel.ShowSmartViewAsync(SmartView.All, Ct);
            AssertCancelled(window, at);
            await showing;

            CollectionAssert.AreEqual(new[] { "A", "B" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_reload_of_the_page_cancels_the_drag()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "A");
        await AddAsync(db, "B");
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await WaitForRowsAsync(window, "A", "B");
            var at = await StartDragAsync(window, list, "A", Bottom(Container(list, "B"), list) - 3);

            await viewModel.LoadAsync(Ct);

            AssertCancelled(window, at);
            CollectionAssert.AreEqual(new[] { "A", "B" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_dragged_Task_deleted_cancels_the_drag()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "A");
        await AddAsync(db, "B");
        await AddAsync(db, "C");
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await WaitForRowsAsync(window, "A", "B", "C");
            var at = await StartDragAsync(window, list, "A", Bottom(Container(list, "C"), list) - 3);

            viewModel.SelectedRow = viewModel.Tasks.Single(t => t.Title == "A");
            await viewModel.DeleteSelectedCommand.ExecuteAsync(null);

            AssertCancelled(window, at);
            await WaitForRowsAsync(window, "B", "C");
            CollectionAssert.AreEqual(new[] { "B", "C" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_dragged_Task_completed_cancels_the_drag()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "A");
        await AddAsync(db, "B");
        await AddAsync(db, "C");
        var (window, viewModel) = Open(db);
        try
        {
            window.View.TaskList.AnimationsEnabled = () => false; // no hold: the row leaves at once
            await viewModel.LoadAsync(Ct);
            var list = await WaitForRowsAsync(window, "A", "B", "C");
            var at = await StartDragAsync(window, list, "A", Bottom(Container(list, "C"), list) - 3);

            await viewModel.ToggleCompleteCommand.ExecuteAsync(viewModel.Tasks.Single(t => t.Title == "A"));

            AssertCancelled(window, at);
            CollectionAssert.AreEqual(new[] { "B", "C" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Another_Task_added_does_not_cancel_and_the_drop_uses_the_gap_under_the_pointer_at_release()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "A");
        await AddAsync(db, "B");
        await AddAsync(db, "C");
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await WaitForRowsAsync(window, "A", "B", "C");
            var drag = window.View.TaskList.TaskDrag;
            await StartDragAsync(window, list, "A", Bottom(Container(list, "B"), list) - 3);

            viewModel.QuickAddTitle = "D";
            await viewModel.AddTaskCommand.ExecuteAsync(null);
            list = await WaitForRowsAsync(window, "A", "B", "C", "D");

            Assert.IsTrue(drag.IsDragging, "Adding another Task does not cancel the drag.");
            Assert.IsNotNull(drag.Picture, "The picture still shows.");

            // Measured against the list as it is now: the bottom half of the new last row is the end.
            var d = Container(list, "D");
            var at = new Point(30, Bottom(d, list) - 3);
            Assert.IsTrue(drag.Move(at));
            Assert.IsTrue(drag.TryRelease(at, out var drop));
            await drop;

            await WaitForRowsAsync(window, "B", "C", "D", "A");
            CollectionAssert.AreEqual(new[] { "B", "C", "D", "A" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    // Presses the row named, moves to y over the list, and checks the drag is on with its picture and line.
    private static async Task<Point> StartDragAsync(MainWindow window, ListView list, string row, double y)
    {
        var drag = window.View.TaskList.TaskDrag;
        await drag.PressAsync(new Point(30, Middle(Container(list, row), list)));
        var at = new Point(30, y);
        Assert.IsTrue(drag.Move(at), "The drag started.");
        Assert.IsNotNull(drag.Picture, "The picture shows.");
        Assert.AreEqual(Visibility.Visible, Line(window).Visibility, "The line shows.");
        return at;
    }

    // The picture and the line are gone, and the pointer neither drags nor drops any more.
    private static void AssertCancelled(MainWindow window, Point at)
    {
        var drag = window.View.TaskList.TaskDrag;
        Assert.IsFalse(drag.IsDragging, "The drag is off.");
        Assert.IsNull(drag.Picture, "The picture is gone.");
        Assert.AreEqual(Visibility.Collapsed, Line(window).Visibility, "The line is gone.");
        Assert.IsFalse(drag.Move(at), "The pointer no longer drags.");
        Assert.IsFalse(drag.TryRelease(at, out _), "The release is no drop.");
    }

    private static Rectangle Line(MainWindow window) => VisualTree.Find<Rectangle>(window.View, "TaskInsertionLine");

    private static double TopOf(FrameworkElement element, UIElement list) =>
        element.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y;

    private static double Middle(FrameworkElement row, UIElement list) => TopOf(row, list) + (row.ActualHeight / 2);

    private static double Bottom(FrameworkElement row, UIElement list) => TopOf(row, list) + row.ActualHeight;

    private static IEnumerable<ListViewItem> ShownRows(ListView list) =>
        VisualTree.Descendants(list).OfType<ListViewItem>()
            .Where(c => list.IndexFromContainer(c) >= 0)
            .OrderBy(list.IndexFromContainer);

    private static string[] Shown(ListView list) =>
        [.. ShownRows(list).Where(c => c.ActualHeight > 0).Select(AutomationProperties.GetName)];

    private static ListViewItem Container(ListView list, string name) =>
        ShownRows(list).Single(c => AutomationProperties.GetName(c) == name);

    private async Task<ListView> WaitForRowsAsync(MainWindow window, params string[] names)
    {
        var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
        await UiWait.UntilAsync(() => Shown(list).SequenceEqual(names),
            () => $"Expected [{string.Join(", ", names)}], was [{string.Join(", ", Shown(list))}].", Ct);
        return list;
    }

    private static async Task<string[]> StoredOrderAsync(TempDatabase db) =>
        [.. (await db.QueryAsync(c => c.Tasks.Where(t => !t.IsCompleted).OrderBy(t => t.SortOrder))).Select(t => t.Title)];

    private static (MainWindow Window, MainViewModel ViewModel) Open(TempDatabase db)
    {
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock,
            dueService: db.Get<IDueService>(), searchService: db.Get<ITaskSearchService>(),
            listService: db.Get<IListService>());
        var window = new MainWindow(viewModel);
        window.Activate();
        return (window, viewModel);
    }

    private async Task<TempDatabase> DatabaseAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return db;
    }

    private async Task AddAsync(TempDatabase db, string title) =>
        await db.Get<ITaskService>().CreateTaskAsync(title, Ct);
}
