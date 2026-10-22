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
using TaskDesk.Views.Dialogs;
using Windows.Foundation;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The drag picture sits over the gap, so the insertion line is drawn again on top of it (ADR 0007, #132's owner
/// decision): at the line's place, as thick and in its colour, at full strength, across the part of the picture the
/// line crosses. It follows the gap, also while the list scrolls under the pointer, and goes when the line hides
/// or the drag ends. In the Task list, the full sidebar and the rail.
/// </summary>
[TestClass]
public sealed class DragLineOverPictureTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task In_the_Task_list_the_line_shows_over_the_picture_at_the_gap_and_goes_with_the_line()
    {
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        foreach (var title in new[] { "A", "B", "C" })
        {
            await db.Get<ITaskService>().CreateTaskAsync(title, Ct);
        }

        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock,
            dueService: db.Get<IDueService>(), searchService: db.Get<ITaskSearchService>(),
            listService: db.Get<IListService>());
        var window = new MainWindow(viewModel);
        window.Activate();
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
            await UiWait.UntilAsync(() => Rows(list).Count == 3 && Rows(list).All(r => r.ActualHeight > 0),
                () => "The Task rows did not show.", Ct);
            var drag = window.View.TaskList.TaskDrag;
            var line = VisualTree.Find<Rectangle>(window.View, "TaskInsertionLine");
            var c = Row(list, "C");

            // Over C's top half, just under its top: the gap before C, inside the picture of A pressed near its top.
            await drag.PressAsync(new Point(30, TopOf(Row(list, "A"), list) + 6));
            drag.Move(new Point(60, TopOf(c, list) + 3));
            await LineOverPictureAsync(drag, line, TopOf(c, list), list, "Over C's top half");

            drag.Move(new Point(60, TopOf(Row(list, "B"), list) + 3));
            await LineOverPictureAsync(drag, line, TopOf(Row(list, "B"), list), list, "Over B's top half");

            drag.Move(new Point(-20, TopOf(c, list) + 3));
            Assert.IsFalse(IsOpen(drag), "Outside the list the line over the picture goes with the line.");

            drag.Move(new Point(60, TopOf(c, list) + 3));
            await LineOverPictureAsync(drag, line, TopOf(c, list), list, "Back over C's top half");
            drag.Cancel();
            Assert.IsNull(drag.LineOverPicture, "The drag's end takes the line over the picture away.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task In_the_sidebar_the_line_shows_over_the_picture_at_the_gap_and_goes_with_the_line(bool rail)
    {
        using var db = await ListsDatabaseAsync("A", "B");
        var window = await OpenWithListsAsync(db, rail);
        try
        {
            var list = VisualTree.Find<ListView>(window.Navigation, "ListsList");
            var drag = window.Navigation.ListDrag;
            var line = VisualTree.Find<Rectangle>(window.Navigation, "ListInsertionLine");
            var b = Row(list, "B");

            await drag.PressAsync(new Point(LeftOf(Row(list, "A"), list) + 12, TopOf(Row(list, "A"), list) + 6));
            drag.Move(new Point(LeftOf(b, list) + 16, TopOf(b, list) + 3));
            await LineOverPictureAsync(drag, line, TopOf(b, list), list, "Over B's top half");

            drag.Move(new Point(list.ActualWidth + 30, TopOf(b, list) + 3));
            Assert.IsFalse(IsOpen(drag), "Outside the Lists the line over the picture goes with the line.");

            drag.Move(new Point(LeftOf(b, list) + 16, TopOf(b, list) + 3));
            await LineOverPictureAsync(drag, line, TopOf(b, list), list, "Back over B's top half");
            Assert.IsTrue(drag.TryRelease(new Point(list.ActualWidth + 30, TopOf(b, list)), out _), "A release ends it.");
            Assert.IsNull(drag.LineOverPicture, "The drag's end takes the line over the picture away.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task While_the_sidebar_scrolls_under_the_pointer_the_line_over_the_picture_keeps_to_the_gap()
    {
        using var db = await ListsDatabaseAsync([.. Enumerable.Range(1, 30).Select(i => $"L{i:00}")]);
        var window = await OpenWithListsAsync(db, rail: false);
        try
        {
            var list = VisualTree.Find<ListView>(window.Navigation, "ListsList");
            var scroll = VisualTree.Find<ScrollViewer>(window.Navigation, "SidebarScroll");
            await UiWait.UntilAsync(() => scroll.ScrollableHeight > 300,
                () => $"The sidebar does not scroll ({scroll.ScrollableHeight}).", Ct);
            var drag = window.Navigation.ListDrag;
            var line = VisualTree.Find<Rectangle>(window.Navigation, "ListInsertionLine");

            // 5 px above the sidebar's visible bottom edge: the Lists scroll, the pointer stays put on the screen.
            var bottom = scroll.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y + scroll.ViewportHeight;
            var l01 = Row(list, "L01");
            await drag.PressAsync(new Point(20, TopOf(l01, list) + (l01.ActualHeight / 2)));
            drag.Move(new Point(20, bottom - 5));
            db.Clock.Advance(TimeSpan.FromMilliseconds(250));
            await UiWait.UntilAsync(() => scroll.VerticalOffset > 100,
                () => $"Scrolled to {scroll.VerticalOffset}.", Ct);

            // The visible line keeps to the gap under the pointer (ListOwnDragTests); the one over the picture with it.
            await UiWait.UntilAsync(
                () => IsOpen(drag) && Math.Abs(WindowTop(line) - drag.LineOverPicture!.VerticalOffset) <= 0.5,
                () => $"Line over the picture at {drag.LineOverPicture?.VerticalOffset}, the line at {WindowTop(line)}.",
                Ct);
        }
        finally
        {
            window.Close();
        }
    }

    // The line over the picture: open, over the line (which is at the gap's top, `gapY` in the list) across the part
    // of the picture the line crosses, as thick, in its colour and at full strength.
    private async Task LineOverPictureAsync(ReorderDrag drag, Rectangle line, double gapY, ListView list, string where)
    {
        await UiWait.UntilAsync(
            () => line.Visibility == Visibility.Visible
                  && Math.Abs(line.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y + (line.ActualHeight / 2) - gapY) <= 1.5,
            () => $"{where}: the line is not at the gap.", Ct);
        await UiWait.UntilAsync(() => IsOpen(drag), () => $"{where}: no line over the picture.", Ct);

        var picture = drag.Picture!;
        var image = (Image)picture.Child;
        var over = drag.LineOverPicture!;
        var shape = (Rectangle)over.Child;
        var lineAt = line.TransformToVisual(null).TransformPoint(new Point(0, 0));
        var left = Math.Max(lineAt.X, picture.HorizontalOffset);
        var right = Math.Min(lineAt.X + line.ActualWidth, picture.HorizontalOffset + image.Width);
        Assert.IsTrue(lineAt.Y >= picture.VerticalOffset && lineAt.Y < picture.VerticalOffset + image.Height,
            $"{where}: the gap is inside the picture (test set-up).");
        Assert.AreEqual(lineAt.Y, over.VerticalOffset, 0.5, $"{where}: at the line's height.");
        Assert.AreEqual(left, over.HorizontalOffset, 0.5, $"{where}: from where the line enters the picture.");
        Assert.AreEqual(right - left, shape.Width, 0.5, $"{where}: to where it leaves it.");
        Assert.AreEqual(line.Height, shape.Height, 0.01, $"{where}: as thick as the line.");
        Assert.AreSame(line.Fill, shape.Fill, $"{where}: in the line's colour.");
        Assert.AreEqual(1.0, shape.Opacity * over.Opacity, 0.001, $"{where}: at full strength.");
        Assert.IsFalse(over.IsHitTestVisible, $"{where}: takes no input.");
        Assert.IsFalse(over.ShouldConstrainToRootBounds, $"{where}: drawn like the picture, in its own layer.");
    }

    private static bool IsOpen(ReorderDrag drag) => drag.LineOverPicture is { IsOpen: true };

    private static double WindowTop(FrameworkElement element) =>
        element.TransformToVisual(null).TransformPoint(new Point(0, 0)).Y;

    private static List<ListViewItem> Rows(ListView list) =>
        [.. Enumerable.Range(0, list.Items.Count).Select(list.ContainerFromIndex).OfType<ListViewItem>()];

    private static ListViewItem Row(ListView list, string name) =>
        Rows(list).Single(r => AutomationProperties.GetName(r) == name);

    private static double TopOf(FrameworkElement element, UIElement list) =>
        element.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y;

    private static double LeftOf(FrameworkElement element, UIElement list) =>
        element.TransformToVisual(list).TransformPoint(new Point(0, 0)).X;

    // A window over the database with its Lists loaded and shown, in the full sidebar or the rail.
    private async Task<MainWindow> OpenWithListsAsync(TempDatabase db, bool rail)
    {
        MainWindow? window = null;
        var viewModel = new MainViewModel(db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, db.Get<IDueService>(),
            db.Get<ITaskSearchService>(), listService: db.Get<IListService>(), taskCreator: db.Get<ITaskCreator>());
        var lists = new SidebarViewModel(viewModel, db.Get<IListService>(), new WinUiDialogs(new DialogService(() => window)));
        window = new MainWindow(viewModel, lists);
        window.Activate();
        try
        {
            window.Navigation.SetRail(rail);
            await viewModel.LoadAsync(Ct);
            await lists.LoadAsync(Ct);
            await UiWait.UntilAsync(
                () => VisualTree.Descendants(window.Navigation).OfType<ListView>().Any(l => l.Name == "ListsList"),
                () => "The Lists list never loaded.", Ct);
            var list = VisualTree.Find<ListView>(window.Navigation, "ListsList");
            await UiWait.UntilAsync(
                () => Rows(list).Count == lists.Lists.Count && Rows(list).All(r => r.IsLoaded && r.ActualHeight > 0),
                () => "The Lists did not show.", Ct);
            return window;
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    // The Default List (Inbox) and these Lists, in this order.
    private async Task<TempDatabase> ListsDatabaseAsync(params string[] names)
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        foreach (var name in names)
        {
            await db.Get<IListService>().CreateListAsync(name, Ct);
        }

        return db;
    }
}
