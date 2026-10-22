using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using Windows.Foundation;

namespace TaskDesk.Views.Tests;

/// <summary>
/// Edge scrolling during the app's own Task drag (ADR 0007): within 40 px of the list's top or bottom edge the
/// list scrolls, faster closer to the edge, and stops outside that band or at the list's end. The insertion line
/// keeps to the gap under the pointer as the rows scroll, and a drop lands in that gap. The drag scrolls on the view
/// model's clock, so each test steps the database's fake clock.
/// </summary>
[TestClass]
public sealed class TaskDragEdgeScrollTests
{
    private const int TaskCount = 50;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task Near_the_bottom_edge_the_list_scrolls_and_a_drop_lands_in_the_gap_the_line_shows()
    {
        using var db = await DatabaseWithTasksAsync();
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await WaitForListAsync(window);
            var scroll = ScrollOf(list);
            var drag = window.View.TaskList.TaskDrag;
            var line = VisualTree.Find<Rectangle>(window.View, "TaskInsertionLine");

            await drag.PressAsync(new Point(30, Middle(Container(list, "T01"), list)));
            var at = new Point(30, list.ActualHeight - 5);
            drag.Move(at);
            await Task.Delay(100, Ct);
            Assert.AreEqual(0, scroll.VerticalOffset, "Nothing scrolls before time passes.");

            // 5 px from the edge is 35/40 of full speed: 700 DIPs a second, so 175 DIPs in a quarter second.
            db.Clock.Advance(TimeSpan.FromMilliseconds(250));
            await OffsetAsync(scroll, 175);

            var (under, before) = RowUnder(list, at.Y);
            var expectedLine = before ? TopOf(under, list) : Bottom(under, list);
            await UiWait.UntilAsync(
                () => line.Visibility == Visibility.Visible && Math.Abs(LineY(line, list) - expectedLine) <= 1.5,
                () => $"Line {line.Visibility} at {LineY(line, list)}, expected {expectedLine}.", Ct);

            Assert.IsTrue(drag.TryRelease(at, out var drop));
            await drop;

            var expected = Titles().Where(t => t != "T01").ToList();
            var name = AutomationProperties.GetName(under);
            expected.Insert(expected.IndexOf(name) + (before ? 0 : 1), "T01");
            CollectionAssert.AreEqual(expected, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Near_the_top_edge_the_list_scrolls_up_faster_closer_to_the_edge_and_stops_at_its_start()
    {
        using var db = await DatabaseWithTasksAsync();
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await WaitForListAsync(window);
            var scroll = ScrollOf(list);
            scroll.ChangeView(null, 400, null, true);
            await OffsetAsync(scroll, 400);
            var drag = window.View.TaskList.TaskDrag;
            var line = VisualTree.Find<Rectangle>(window.View, "TaskInsertionLine");
            var press = new Point(30, list.ActualHeight / 2);

            await drag.PressAsync(press);
            drag.Move(new Point(30, press.Y + 20));

            // 30 px from the top edge is a quarter of full speed: 200 DIPs a second, 50 DIPs in a quarter second.
            drag.Move(new Point(30, 30));
            db.Clock.Advance(TimeSpan.FromMilliseconds(250));
            await OffsetAsync(scroll, 350);

            // On the edge, full speed: 800 DIPs a second, 200 DIPs in a quarter second.
            drag.Move(new Point(30, 0));
            db.Clock.Advance(TimeSpan.FromMilliseconds(250));
            await OffsetAsync(scroll, 150);

            db.Clock.Advance(TimeSpan.FromMilliseconds(250));
            await OffsetAsync(scroll, 0);
            db.Clock.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(100, Ct);
            Assert.AreEqual(0, scroll.VerticalOffset, "At the list's start it stops.");

            var first = Container(list, "T01");
            await UiWait.UntilAsync(
                () => line.Visibility == Visibility.Visible && Math.Abs(LineY(line, list) - TopOf(first, list)) <= 1.5,
                () => $"Line {line.Visibility} at {LineY(line, list)}, expected the first gap at {TopOf(first, list)}.",
                Ct);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Outside_the_edge_band_and_outside_the_list_a_drag_does_not_scroll()
    {
        using var db = await DatabaseWithTasksAsync();
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await WaitForListAsync(window);
            var scroll = ScrollOf(list);
            var drag = window.View.TaskList.TaskDrag;

            await drag.PressAsync(new Point(30, Middle(Container(list, "T01"), list)));
            var points = new[]
            {
                new Point(30, list.ActualHeight - DragRules.EdgeScrollBand - 1), // just above the bottom band
                new Point(-20, list.ActualHeight - 5), // beside the list, level with its bottom band
                new Point(30, list.ActualHeight + 5), // below the list
            };
            foreach (var at in points)
            {
                drag.Move(at);
                db.Clock.Advance(TimeSpan.FromMilliseconds(250));
            }

            await Task.Delay(100, Ct);
            Assert.AreEqual(0, scroll.VerticalOffset);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Once_the_drag_ends_the_list_scrolls_no_further(bool released)
    {
        using var db = await DatabaseWithTasksAsync();
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await WaitForListAsync(window);
            var scroll = ScrollOf(list);
            var drag = window.View.TaskList.TaskDrag;

            await drag.PressAsync(new Point(30, Middle(Container(list, "T01"), list)));
            var at = new Point(30, list.ActualHeight - 10);
            drag.Move(at);
            db.Clock.Advance(TimeSpan.FromMilliseconds(100));
            await OffsetAsync(scroll, 60); // 600 DIPs a second, 10 px from the edge.

            if (released)
            {
                Assert.IsTrue(drag.TryRelease(at, out var drop));
                await drop;
            }
            else
            {
                drag.Cancel();
            }

            // A drop moves rows, which may move the offset by itself; once it settles, the clock scrolls nothing.
            await Task.Delay(100, Ct);
            var settled = scroll.VerticalOffset;
            db.Clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(100, Ct);
            Assert.AreEqual(settled, scroll.VerticalOffset, 0.01, "The list stays where the drag left it.");
        }
        finally
        {
            window.Close();
        }
    }

    private async Task OffsetAsync(ScrollViewer scroll, double expected) =>
        await UiWait.UntilAsync(() => Math.Abs(scroll.VerticalOffset - expected) <= 1,
            () => $"Scrolled to {scroll.VerticalOffset}, expected {expected}.", Ct);

    private static double LineY(Rectangle line, ListView list) => TopOf(line, list) + (line.ActualHeight / 2);

    // The shown row under y, and whether y is over its top half.
    private static (ListViewItem Row, bool Before) RowUnder(ListView list, double y)
    {
        var row = ShownRows(list).Single(c => c.ActualHeight > 0 && TopOf(c, list) <= y && y < Bottom(c, list));
        return (row, y < Middle(row, list));
    }

    private static ScrollViewer ScrollOf(ListView list) => VisualTree.Descendants(list).OfType<ScrollViewer>().First();

    private static double TopOf(FrameworkElement element, UIElement list) =>
        element.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y;

    private static double Middle(FrameworkElement row, UIElement list) => TopOf(row, list) + (row.ActualHeight / 2);

    private static double Bottom(FrameworkElement row, UIElement list) => TopOf(row, list) + row.ActualHeight;

    private static IEnumerable<ListViewItem> ShownRows(ListView list) =>
        VisualTree.Descendants(list).OfType<ListViewItem>()
            .Where(c => list.IndexFromContainer(c) >= 0)
            .OrderBy(list.IndexFromContainer);

    private static ListViewItem Container(ListView list, string name) =>
        ShownRows(list).Single(c => AutomationProperties.GetName(c) == name);

    private static List<string> Titles() => [.. Enumerable.Range(1, TaskCount).Select(i => $"T{i:00}")];

    // Waits until the Task list shows its first row and has more rows than fit, and returns it.
    private async Task<ListView> WaitForListAsync(MainWindow window)
    {
        var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
        await UiWait.UntilAsync(
            () => ShownRows(list).Any(c => AutomationProperties.GetName(c) == "T01" && c.ActualHeight > 0)
                  && ScrollOf(list).ScrollableHeight > 450,
            () => "The list did not show more Tasks than fit.", Ct);
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

    // T01 to T50 in the Default List, in that order.
    private async Task<TempDatabase> DatabaseWithTasksAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        var service = db.Get<ITaskService>();
        foreach (var title in Titles())
        {
            await service.CreateTaskAsync(title, Ct);
        }

        return db;
    }
}
