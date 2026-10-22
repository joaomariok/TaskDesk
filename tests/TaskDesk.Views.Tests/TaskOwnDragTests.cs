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
using Windows.System;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The app's own reordering drag in the Task list (ADR 0007), driven through its controller with points relative
/// to the list: the picture follows the pointer with the press point under it, the insertion line follows the
/// gaps, a release over the list drops and outside it cancels, Esc cancels, and nothing else changes on the way.
/// The real mouse drag is a FlaUI test (ReorderTests).
/// </summary>
[TestClass]
public sealed class TaskOwnDragTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task A_drag_shows_the_rows_picture_at_80_percent_with_the_press_point_under_the_pointer()
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
            var a = Container(list, "A");
            var press = new Point(30, TopOf(a, list) + 10);

            await drag.PressAsync(press);
            Assert.IsNull(drag.Picture, "No picture on a press alone.");
            var pointer = new Point(press.X + 25, press.Y + 50);
            Assert.IsTrue(drag.Move(pointer), "Moving past the threshold starts the drag.");

            var picture = drag.Picture;
            Assert.IsNotNull(picture, "The picture shows.");
            Assert.IsTrue(picture.IsOpen);
            Assert.IsFalse(picture.ShouldConstrainToRootBounds, "The picture may leave the window.");
            Assert.IsFalse(picture.IsHitTestVisible);
            var image = (Image)picture.Child;
            Assert.AreEqual(0.8, image.Opacity, 0.001);
            Assert.AreEqual(a.ActualWidth, image.Width, 0.5);
            Assert.AreEqual(a.ActualHeight, image.Height, 0.5);
            AssertPressPointUnderPointer(picture, list, pointer, new Point(press.X - LeftOf(a, list), 10));

            var next = new Point(press.X - 400, press.Y + 20);
            drag.Move(next);
            AssertPressPointUnderPointer(picture, list, next, new Point(press.X - LeftOf(a, list), 10));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task While_dragged_the_rows_and_the_selection_stay_as_they_were()
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
            viewModel.SelectedRow = viewModel.Tasks.Single(t => t.Title == "B");
            await UiWait.UntilAsync(() => list.SelectedItem == viewModel.SelectedRow, () => "B was not selected.", Ct);
            var drag = window.View.TaskList.TaskDrag;
            var a = Container(list, "A");

            await drag.PressAsync(new Point(30, Middle(a, list)));
            drag.Move(new Point(30, Middle(Container(list, "C"), list) + 8));

            Assert.AreEqual(1.0, a.Opacity, "The dragged row does not fade.");
            Assert.AreSame(viewModel.Tasks.Single(t => t.Title == "B"), list.SelectedItem, "The list keeps its selection.");
            Assert.AreEqual("B", viewModel.SelectedTask?.Title);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, Shown(list));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_insertion_line_follows_the_gap_under_the_pointer_and_hides_outside_the_list()
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
            var line = VisualTree.Find<Rectangle>(window.View, "TaskInsertionLine");
            var b = Container(list, "B");
            var c = Container(list, "C");

            await drag.PressAsync(new Point(30, Middle(Container(list, "A"), list)));
            drag.Move(new Point(30, TopOf(c, list) + 3));
            await LineAtAsync(line, list, TopOf(c, list), "Over C's top half");

            drag.Move(new Point(30, TopOf(b, list) + b.ActualHeight - 3));
            await LineAtAsync(line, list, TopOf(c, list), "Over B's bottom half");

            drag.Move(new Point(30, Bottom(c, list) + 3));
            await LineAtAsync(line, list, Bottom(c, list), "Below the rows");

            drag.Move(new Point(-20, Middle(b, list)));
            Assert.AreEqual(Visibility.Collapsed, line.Visibility, "Outside the list the line hides.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_release_over_the_list_drops_into_the_gap_and_the_selected_Task_stays_selected()
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
            var a = viewModel.Tasks.Single(t => t.Title == "A");
            viewModel.SelectedRow = a;
            var drag = window.View.TaskList.TaskDrag;
            var line = VisualTree.Find<Rectangle>(window.View, "TaskInsertionLine");
            var c = Container(list, "C");

            await drag.PressAsync(new Point(30, Middle(Container(list, "A"), list)));
            var at = new Point(30, TopOf(c, list) + c.ActualHeight - 3);
            drag.Move(at);
            Assert.IsTrue(drag.TryRelease(at, out var drop), "A release ends a drag.");
            Assert.IsNull(drag.Picture, "The picture goes at the release.");
            await drop;

            Assert.AreEqual(Visibility.Collapsed, line.Visibility);
            await WaitForRowsAsync(window, "B", "C", "A");
            CollectionAssert.AreEqual(new[] { "B", "C", "A" }, await StoredOrderAsync(db));
            Assert.AreSame(a, viewModel.SelectedTask, "The dropped Task stays selected.");
            await UiWait.UntilAsync(() => list.SelectedItem == a, () => "The list lost the selection.", Ct);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_release_over_a_completed_row_below_the_open_rows_moves_the_Task_to_the_end()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "A");
        await AddAsync(db, "B");
        await AddCompletedAsync(db, "Done");
        var (window, viewModel) = Open(db);
        try
        {
            viewModel.IsCompletedExpanded = true;
            await viewModel.LoadAsync(Ct);
            var list = await WaitForRowsAsync(window, "A", "B", "Done");
            var drag = window.View.TaskList.TaskDrag;

            await drag.PressAsync(new Point(30, Middle(Container(list, "A"), list)));
            var at = new Point(30, Middle(Container(list, "Done"), list));
            drag.Move(at);
            Assert.IsTrue(drag.TryRelease(at, out var drop));
            await drop;

            await UiWait.UntilAsync(
                () => viewModel.Tasks.Select(t => t.Title).SequenceEqual(["B", "A"]),
                () => "A did not move to the end.", Ct);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_release_outside_the_list_cancels_and_moves_nothing()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "A");
        await AddAsync(db, "B");
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await WaitForRowsAsync(window, "A", "B");
            var drag = window.View.TaskList.TaskDrag;
            var line = VisualTree.Find<Rectangle>(window.View, "TaskInsertionLine");

            await drag.PressAsync(new Point(30, Middle(Container(list, "A"), list)));
            drag.Move(new Point(30, Bottom(Container(list, "B"), list) - 3));
            var outside = new Point(-40, Middle(Container(list, "B"), list));
            drag.Move(outside);
            Assert.IsTrue(drag.TryRelease(outside, out var drop), "The release ends the drag.");
            await drop;

            Assert.IsNull(drag.Picture);
            Assert.IsFalse(drag.IsDragging);
            Assert.AreEqual(Visibility.Collapsed, line.Visibility);
            CollectionAssert.AreEqual(new[] { "A", "B" }, await StoredOrderAsync(db));
            CollectionAssert.AreEqual(new[] { "A", "B" }, Shown(list));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Esc_cancels_a_drag_at_once_and_a_later_release_moves_nothing()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "A");
        await AddAsync(db, "B");
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await WaitForRowsAsync(window, "A", "B");
            var taskList = window.View.TaskList;
            var drag = taskList.TaskDrag;
            var line = VisualTree.Find<Rectangle>(window.View, "TaskInsertionLine");
            Assert.IsFalse(taskList.TryShortcut(VirtualKey.Escape, VirtualKeyModifiers.None), "Esc does nothing with no drag.");

            await drag.PressAsync(new Point(30, Middle(Container(list, "A"), list)));
            var at = new Point(30, Bottom(Container(list, "B"), list) - 3);
            drag.Move(at);
            Assert.AreEqual(Visibility.Visible, line.Visibility);

            Assert.IsTrue(taskList.TryShortcut(VirtualKey.Escape, VirtualKeyModifiers.None), "Esc cancels the drag.");
            Assert.IsNull(drag.Picture, "The picture goes at once.");
            Assert.AreEqual(Visibility.Collapsed, line.Visibility, "The line goes at once.");
            Assert.IsFalse(drag.Move(at), "The pointer no longer drags.");
            Assert.IsFalse(drag.TryRelease(at, out _), "The release is no drag.");

            CollectionAssert.AreEqual(new[] { "A", "B" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_press_that_moves_within_the_threshold_is_not_a_drag()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "A");
        await AddAsync(db, "B");
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await WaitForRowsAsync(window, "A", "B");
            var drag = window.View.TaskList.TaskDrag;
            var press = new Point(30, Middle(Container(list, "A"), list));

            await drag.PressAsync(press);
            Assert.IsFalse(drag.Move(new Point(press.X + 1, press.Y + 1)));
            Assert.IsNull(drag.Picture);

            Assert.IsFalse(drag.TryRelease(new Point(press.X + 1, press.Y + 1), out _), "A click stays a click.");
            Assert.IsFalse(drag.LastPressDragged);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task An_open_Tasks_picture_is_its_block_capped_at_five_rows()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "one", "two");
        await AddAsync(db, "big", "s1", "s2", "s3", "s4", "s5", "s6");
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            viewModel.OpenTask(viewModel.Tasks.Single(t => t.Title == "trip"));
            viewModel.OpenTask(viewModel.Tasks.Single(t => t.Title == "big"));
            var list = await WaitForRowsAsync(
                window, "trip", "Subtask one", "Subtask two",
                "big", "Subtask s1", "Subtask s2", "Subtask s3", "Subtask s4", "Subtask s5", "Subtask s6");
            var drag = window.View.TaskList.TaskDrag;

            var trip = Container(list, "trip");
            await drag.PressAsync(new Point(30, Middle(trip, list)));
            drag.Move(new Point(30, Middle(trip, list) + 40));
            var block = Bottom(Container(list, "Subtask two"), list) - TopOf(trip, list);
            Assert.AreEqual(block, ((Image)drag.Picture!.Child).Height, 0.5, "trip's whole block.");
            drag.Cancel();

            var big = Container(list, "big");
            await drag.PressAsync(new Point(30, Middle(big, list)));
            drag.Move(new Point(30, Middle(big, list) - 40));
            var five = Bottom(Container(list, "Subtask s4"), list) - TopOf(big, list);
            Assert.AreEqual(five, ((Image)drag.Picture!.Child).Height, 0.5, "big's first five rows.");
            drag.Cancel();
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Subtask_rows_and_completed_rows_do_not_drag()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack");
        await AddCompletedAsync(db, "Done");
        var (window, viewModel) = Open(db);
        try
        {
            viewModel.IsCompletedExpanded = true;
            await viewModel.LoadAsync(Ct);
            await WaitForRowsAsync(window, "trip", "Done");
            viewModel.OpenTask(viewModel.Tasks.Single(t => t.Title == "trip"));
            var list = await WaitForRowsAsync(window, "trip", "Subtask pack", "Done");
            var drag = window.View.TaskList.TaskDrag;

            foreach (var name in new[] { "Subtask pack", "Done" })
            {
                var press = new Point(30, Middle(Container(list, name), list));
                await drag.PressAsync(press);
                Assert.IsFalse(drag.Move(new Point(press.X, press.Y - 60)), $"{name} does not drag.");
                Assert.IsNull(drag.Picture, name);
                Assert.IsFalse(drag.TryRelease(press, out _), name);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Nothing_drags_where_reordering_is_off()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "A");
        await AddAsync(db, "B");
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.ShowSmartViewAsync(SmartView.All, Ct);
            var list = await WaitForRowsAsync(window, "A", "B");
            Assert.IsFalse(viewModel.IsReorderEnabled);
            var drag = window.View.TaskList.TaskDrag;
            var press = new Point(30, Middle(Container(list, "A"), list));

            await drag.PressAsync(press);
            Assert.IsFalse(drag.Move(new Point(press.X, press.Y + 60)));
            Assert.IsNull(drag.Picture);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    [DataRow(ElementTheme.Light)]
    [DataRow(ElementTheme.Dark)]
    public async Task The_picture_is_backed_by_the_themes_solid_window_background(ElementTheme theme)
    {
        var expected = (await ThemeColours.ResolveAsync(theme, ["SolidBackgroundFillColorBaseBrush"], Ct))
            ["SolidBackgroundFillColorBaseBrush"];
        using var db = await DatabaseAsync();
        await AddAsync(db, "A");
        var (window, viewModel) = Open(db);
        try
        {
            ((FrameworkElement)window.Content).RequestedTheme = theme;
            await viewModel.LoadAsync(Ct);
            var list = await WaitForRowsAsync(window, "A");
            var drag = window.View.TaskList.TaskDrag;
            var press = new Point(30, Middle(Container(list, "A"), list));

            await drag.PressAsync(press);
            drag.Move(new Point(press.X, press.Y + 60));

            // The row draws nothing at its top-left corner, so the backing shows there.
            var source = (Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap)((Image)drag.Picture!.Child).Source;
            var corner = new byte[4];
            using (var stream = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsStream(source.PixelBuffer))
            {
                await stream.ReadExactlyAsync(corner, Ct);
            }

            Assert.AreEqual(expected, Windows.UI.Color.FromArgb(corner[3], corner[2], corner[1], corner[0]));
        }
        finally
        {
            window.Close();
        }
    }

    // The picture's press point (its offset into the picture) sits under the pointer, in window coordinates.
    private static void AssertPressPointUnderPointer(
        Microsoft.UI.Xaml.Controls.Primitives.Popup picture, ListView list, Point pointer, Point pressOffset)
    {
        var inWindow = list.TransformToVisual(null).TransformPoint(pointer);
        Assert.AreEqual(inWindow.X, picture.HorizontalOffset + pressOffset.X, 0.5, "Press point x.");
        Assert.AreEqual(inWindow.Y, picture.VerticalOffset + pressOffset.Y, 0.5, "Press point y.");
    }

    private async Task LineAtAsync(Rectangle line, ListView list, double expected, string where) =>
        await UiWait.UntilAsync(
            () => line.Visibility == Visibility.Visible && Math.Abs(TopOf(line, list) + (line.ActualHeight / 2) - expected) <= 1.5,
            () => $"{where}: line shown {line.Visibility}, at {TopOf(line, list) + (line.ActualHeight / 2)}, expected {expected}.",
            Ct);

    private static double TopOf(FrameworkElement element, UIElement list) =>
        element.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y;

    private static double LeftOf(FrameworkElement element, UIElement list) =>
        element.TransformToVisual(list).TransformPoint(new Point(0, 0)).X;

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

    // Waits until the Task list shows exactly these rows (by their accessible names), realised, and returns it.
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

    private async Task AddCompletedAsync(TempDatabase db, string title)
    {
        var service = db.Get<ITaskService>();
        var task = await service.CreateTaskAsync(title, Ct);
        await service.SetCompletedAsync(task.Id, true, Ct);
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
