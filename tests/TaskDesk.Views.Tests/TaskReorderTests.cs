using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
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
/// Drag to reorder in the open Task list: where a drop lands maps to the right move through the view model's
/// move use case, the order is persisted, and reordering is off outside a plain List view. The real mouse drag
/// is a FlaUI test (ReorderTests).
/// </summary>
[TestClass]
public sealed class TaskReorderTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task Dropping_a_row_on_another_moves_it_there_and_the_order_is_persisted()
    {
        using var db = await DatabaseAsync();
        foreach (var title in new[] { "A", "B", "C", "D" })
        {
            await AddAsync(db, title);
        }

        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            await UiWait.UntilAsync(() => viewModel.Tasks.Count == 4, () => "The Tasks did not load.", Ct);
            var taskList = window.View.TaskList;

            await taskList.DropAsync(viewModel.Tasks[0], viewModel.Tasks[2]);
            CollectionAssert.AreEqual(new[] { "B", "C", "A", "D" }, viewModel.Tasks.Select(t => t.Title).ToArray());
            CollectionAssert.AreEqual(new[] { "B", "C", "A", "D" }, await StoredOrderAsync(db));

            await taskList.DropAsync(viewModel.Tasks[3], viewModel.Tasks[0]);
            CollectionAssert.AreEqual(new[] { "D", "B", "C", "A" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Dropping_on_empty_space_moves_the_row_to_the_end()
    {
        using var db = await DatabaseAsync();
        foreach (var title in new[] { "A", "B", "C" })
        {
            await AddAsync(db, title);
        }

        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            await UiWait.UntilAsync(() => viewModel.Tasks.Count == 3, () => "The Tasks did not load.", Ct);

            await window.View.TaskList.DropAsync(viewModel.Tasks[0], null);

            CollectionAssert.AreEqual(new[] { "B", "C", "A" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_drop_position_maps_to_the_row_under_it_or_to_none_below_the_rows()
    {
        using var db = await DatabaseAsync();
        foreach (var title in new[] { "A", "B", "C" })
        {
            await AddAsync(db, title);
        }

        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
            await UiWait.UntilAsync(
                () => Enumerable.Range(0, 3).All(i => list.ContainerFromIndex(i) is FrameworkElement { ActualHeight: > 0 }),
                () => "The rows were not realized.", Ct);
            var taskList = window.View.TaskList;

            for (var i = 0; i < 3; i++)
            {
                var row = (FrameworkElement)list.ContainerFromIndex(i);
                var top = row.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y;
                Assert.AreSame(viewModel.Tasks[i], taskList.RowAt(new Point(20, top + (row.ActualHeight / 2))), $"Row {i}.");
            }

            Assert.IsNull(taskList.RowAt(new Point(20, list.ActualHeight + 500)));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Only_open_rows_take_part_in_a_drag_with_Completed_N_expanded()
    {
        using var db = await DatabaseAsync();
        foreach (var title in new[] { "A", "B" })
        {
            await AddAsync(db, title);
        }

        await AddAsync(db, "Done", completed: true);
        var (window, viewModel) = Open(db);
        try
        {
            viewModel.IsCompletedExpanded = true;
            await viewModel.LoadAsync(Ct);
            var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
            await UiWait.UntilAsync(
                () => Enumerable.Range(0, 3).All(i => list.ContainerFromIndex(i) is FrameworkElement { ActualHeight: > 0 }),
                () => "The rows were not realized.", Ct);
            var taskList = window.View.TaskList;

            // Over a completed row a drop finds no row, so it lands at the end of the open rows.
            var done = (FrameworkElement)list.ContainerFromItem(viewModel.CompletedTasks.Single());
            var top = done.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y;
            Assert.IsNull(taskList.RowAt(new Point(20, top + (done.ActualHeight / 2))));

            await taskList.DropAsync(viewModel.Tasks[0], null);
            CollectionAssert.AreEqual(new[] { "B", "A" }, viewModel.Tasks.Select(t => t.Title).ToArray());

            // A completed row is not moved among the open ones.
            await taskList.DropAsync(viewModel.CompletedTasks.Single(), viewModel.Tasks[0]);
            CollectionAssert.AreEqual(new[] { "B", "A" }, viewModel.Tasks.Select(t => t.Title).ToArray());
            Assert.AreEqual("Done", viewModel.CompletedTasks.Single().Title);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Dropping_does_nothing_where_reordering_is_off()
    {
        using var db = await DatabaseAsync();
        var today = DateOnly.FromDateTime(db.Clock.GetUtcNow().Date);
        await AddAsync(db, "A", due: today);
        await AddAsync(db, "B", due: today);
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.ShowSmartViewAsync(SmartView.Today, Ct);
            await UiWait.UntilAsync(() => viewModel.Tasks.Count == 2, () => "The Tasks did not load.", Ct);
            Assert.IsFalse(viewModel.IsReorderEnabled);

            await window.View.TaskList.DropAsync(viewModel.Tasks[0], viewModel.Tasks[1]);

            CollectionAssert.AreEqual(new[] { "A", "B" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_pointer_over_a_rows_top_half_is_the_gap_before_it_and_over_its_bottom_half_the_gap_after_it()
    {
        using var db = await DatabaseAsync();
        foreach (var title in new[] { "A", "B", "C" })
        {
            await AddAsync(db, title);
        }

        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await RealizedAsync(window, 3);
            var taskList = window.View.TaskList;

            for (var i = 0; i < 3; i++)
            {
                var row = (FrameworkElement)list.ContainerFromIndex(i);
                var top = TopOf(row, list);
                Assert.AreEqual(i, taskList.GapAt(new Point(20, top + 2)), $"Row {i}, top half.");
                Assert.AreEqual(i + 1, taskList.GapAt(new Point(20, top + row.ActualHeight - 2)), $"Row {i}, bottom half.");
            }

            Assert.AreEqual(3, taskList.GapAt(new Point(20, list.ActualHeight + 500)), "Below the rows.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_drop_below_the_open_rows_over_a_completed_row_moves_the_Task_to_the_end()
    {
        using var db = await DatabaseAsync();
        foreach (var title in new[] { "A", "B" })
        {
            await AddAsync(db, title);
        }

        await AddAsync(db, "Done", completed: true);
        var (window, viewModel) = Open(db);
        try
        {
            viewModel.IsCompletedExpanded = true;
            await viewModel.LoadAsync(Ct);
            var list = await RealizedAsync(window, 3);
            var taskList = window.View.TaskList;
            var done = (FrameworkElement)list.ContainerFromItem(viewModel.CompletedTasks.Single());

            var gap = taskList.GapAt(new Point(20, TopOf(done, list) + (done.ActualHeight / 2)));
            Assert.AreEqual(2, gap);
            await taskList.DropIntoGapAsync(viewModel.Tasks[0], gap);

            CollectionAssert.AreEqual(new[] { "B", "A", "Done" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_drop_into_a_gap_saves_the_order_and_a_drop_into_the_Tasks_own_gap_changes_nothing()
    {
        using var db = await DatabaseAsync();
        foreach (var title in new[] { "A", "B", "C" })
        {
            await AddAsync(db, title);
        }

        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            await RealizedAsync(window, 3);
            var taskList = window.View.TaskList;

            await taskList.DropIntoGapAsync(viewModel.Tasks[2], 0);
            CollectionAssert.AreEqual(new[] { "C", "A", "B" }, await StoredOrderAsync(db));

            await taskList.DropIntoGapAsync(viewModel.Tasks[1], 1);
            await taskList.DropIntoGapAsync(viewModel.Tasks[1], 2);
            CollectionAssert.AreEqual(new[] { "C", "A", "B" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task While_a_Task_is_dragged_the_insertion_line_sits_at_a_rows_top_or_under_the_last_open_row()
    {
        using var db = await DatabaseAsync();
        foreach (var title in new[] { "A", "B" })
        {
            await AddAsync(db, title);
        }

        await AddAsync(db, "Done", completed: true);
        var (window, viewModel) = Open(db);
        try
        {
            viewModel.IsCompletedExpanded = true;
            await viewModel.LoadAsync(Ct);
            var list = await RealizedAsync(window, 3);
            var taskList = window.View.TaskList;
            var line = VisualTree.Find<Rectangle>(window.View, "TaskInsertionLine");
            Assert.IsFalse(IsShown(line), "No line before a drag.");
            Assert.IsFalse(line.IsHitTestVisible, "The line takes no input.");
            Assert.AreEqual(2, line.Height);

            for (var gap = 0; gap <= 2; gap++)
            {
                var row = (FrameworkElement)list.ContainerFromIndex(Math.Min(gap, 1));
                var expected = TopOf(row, list) + (gap == 2 ? row.ActualHeight : 0);
                taskList.ShowDropGap(gap);
                await UiWait.UntilAsync(
                    () => IsShown(line) && Math.Abs(LineMiddle(line, list) - expected) <= 1.5,
                    () => $"Gap {gap}: line shown {IsShown(line)} at {LineMiddle(line, list)}, expected {expected}.", Ct);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_insertion_line_hides_after_a_drop_and_when_the_drag_leaves_the_list()
    {
        using var db = await DatabaseAsync();
        foreach (var title in new[] { "A", "B", "C" })
        {
            await AddAsync(db, title);
        }

        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await RealizedAsync(window, 3);
            var taskList = window.View.TaskList;
            var line = VisualTree.Find<Rectangle>(window.View, "TaskInsertionLine");

            taskList.ShowDropGap(1);
            Assert.IsTrue(IsShown(line), "Shown during the drag.");
            await taskList.DropIntoGapAsync(viewModel.Tasks[0], 3);
            Assert.IsFalse(IsShown(line), "Hidden after a drop.");

            // The move re-realizes the rows; the line needs them back to find a gap.
            await UiWait.UntilAsync(
                () =>
                {
                    taskList.ShowDropGap(1);
                    return IsShown(line);
                },
                () => "The line did not show on the next drag.", Ct);
            taskList.LeaveTaskDrag(new Point(20, list.ActualHeight / 2));
            Assert.IsTrue(IsShown(line), "Moving between rows keeps the line.");
            taskList.LeaveTaskDrag(new Point(20, list.ActualHeight + 10));
            Assert.IsFalse(IsShown(line), "Hidden once the drag leaves the list.");
        }
        finally
        {
            window.Close();
        }
    }

    private static bool IsShown(UIElement element) => element.Visibility == Visibility.Visible;

    private static double TopOf(FrameworkElement row, UIElement list) =>
        row.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y;

    private static double LineMiddle(FrameworkElement line, UIElement list) =>
        TopOf(line, list) + (line.ActualHeight / 2);

    private async Task<ListView> RealizedAsync(MainWindow window, int rows)
    {
        var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
        await UiWait.UntilAsync(
            () => Enumerable.Range(0, rows).All(i => list.ContainerFromIndex(i) is FrameworkElement { ActualHeight: > 0 }),
            () => "The rows were not realized.", Ct);
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

    private static async Task AddAsync(TempDatabase db, string title, DateOnly? due = null, bool completed = false)
    {
        var listId = (await db.QueryAsync(c => c.Lists.Where(l => l.IsDefault))).Single().Id;
        await using var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync();
        var now = db.Clock.GetUtcNow().UtcDateTime;
        var order = await context.Tasks.CountAsync();
        context.Tasks.Add(new TaskItem
        {
            Id = Guid.CreateVersion7(),
            ListId = listId,
            Title = title,
            DueDate = due,
            IsCompleted = completed,
            CompletedUtc = completed ? now : null,
            SortOrder = order,
            CreatedUtc = now,
            UpdatedUtc = now,
        });
        await context.SaveChangesAsync();
    }
}
