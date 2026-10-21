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
/// Dragging a Task while Tasks are open: an open Task and its Subtask rows are one block that a drop never splits,
/// so a pointer inside a block lands after it and the insertion line shows under its last Subtask row. The real
/// mouse drag is a FlaUI test (ReorderTests).
/// </summary>
[TestClass]
public sealed class TaskBlockDragTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task A_pointer_inside_an_open_Tasks_block_is_the_gap_after_it_and_the_line_shows_at_the_blocks_end()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack", "go");
        await AddAsync(db, "other");
        await AddAsync(db, "last", "x");
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            viewModel.OpenTask(viewModel.Tasks.Single(t => t.Title == "trip"));
            viewModel.OpenTask(viewModel.Tasks.Single(t => t.Title == "last"));
            var list = await WaitForRowsAsync(
                window, "trip", "Subtask pack", "Subtask go", "other", "last", "Subtask x");
            var taskList = window.View.TaskList;
            var line = VisualTree.Find<Rectangle>(window.View, "TaskInsertionLine");

            // Inside trip's block: its Task row's bottom half and each of its Subtask rows.
            var trip = Container(list, "trip");
            var inside = new[]
            {
                TopOf(trip, list) + trip.ActualHeight - 2,
                Middle(Container(list, "Subtask pack"), list),
                Middle(Container(list, "Subtask go"), list),
            };
            var tripEnd = Bottom(Container(list, "Subtask go"), list);
            foreach (var y in inside)
            {
                var gap = taskList.GapAt(new Point(20, y));
                Assert.AreEqual(1, gap, $"At {y}: the gap after trip's block.");
                taskList.ShowDropGap(gap);
                await LineAtAsync(line, list, tripEnd, $"At {y}");
            }

            // Inside the last block: the line under its last Subtask row, not under its Task row.
            var lastY = Middle(Container(list, "Subtask x"), list);
            Assert.AreEqual(3, taskList.GapAt(new Point(20, lastY)), "The last block: the end.");
            taskList.ShowDropGap(3);
            await LineAtAsync(line, list, Bottom(Container(list, "Subtask x"), list), "The last block");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_dragged_open_Task_dropped_inside_another_block_lands_after_it_with_its_Subtask_rows()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack", "go");
        await AddAsync(db, "other");
        await AddAsync(db, "last", "x");
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            viewModel.OpenTask(viewModel.Tasks.Single(t => t.Title == "trip"));
            viewModel.OpenTask(viewModel.Tasks.Single(t => t.Title == "last"));
            var list = await WaitForRowsAsync(
                window, "trip", "Subtask pack", "Subtask go", "other", "last", "Subtask x");
            var taskList = window.View.TaskList;
            var gap = taskList.GapAt(new Point(20, Middle(Container(list, "Subtask pack"), list)));

            await taskList.DropIntoGapAsync(viewModel.Tasks.Single(t => t.Title == "last"), gap);

            await WaitForRowsAsync(window, "trip", "Subtask pack", "Subtask go", "last", "Subtask x", "other");
            CollectionAssert.AreEqual(new[] { "trip", "last", "other" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    private async Task LineAtAsync(Rectangle line, ListView list, double expected, string where) =>
        await UiWait.UntilAsync(
            () => line.Visibility == Visibility.Visible && Math.Abs(TopOf(line, list) + (line.ActualHeight / 2) - expected) <= 1.5,
            () => $"{where}: line shown {line.Visibility}, at {TopOf(line, list) + (line.ActualHeight / 2)}, expected {expected}.",
            Ct);

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

    // Waits until the Task list shows exactly these rows (by their accessible names), realised, and returns it.
    private async Task<ListView> WaitForRowsAsync(MainWindow window, params string[] names)
    {
        var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
        List<string> Shown() => [.. ShownRows(list).Where(c => c.ActualHeight > 0).Select(AutomationProperties.GetName)];
        await UiWait.UntilAsync(() => Shown().SequenceEqual(names),
            () => $"Expected [{string.Join(", ", names)}], was [{string.Join(", ", Shown())}].", Ct);
        return list;
    }

    private static async Task<string[]> StoredOrderAsync(TempDatabase db) =>
        [.. (await db.QueryAsync(c => c.Tasks.OrderBy(t => t.SortOrder))).Select(t => t.Title)];

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

    // A Task in the Default List with its Subtasks in this order.
    private async Task AddAsync(TempDatabase db, string title, params string[] subtasks)
    {
        var service = db.Get<ITaskService>();
        var task = await service.CreateTaskAsync(title, Ct);
        foreach (var subtask in subtasks)
        {
            await service.AddSubtaskAsync(task.Id, subtask, Ct);
        }
    }
}
