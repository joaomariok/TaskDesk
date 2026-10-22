using Microsoft.Extensions.Time.Testing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using TaskDesk.Views.Dialogs;
using Windows.Foundation;
using Windows.System;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The app's own reordering drag on the sidebar's Lists (ADR 0007, #137), driven through the sidebar's drag
/// controller with points relative to the Lists list, in the full sidebar and in the rail: the picture is the List's
/// row at 80 % with the press point under the pointer, the insertion line follows the gaps, a release over the Lists
/// drops and saves, and Esc, a release outside the Lists, the window losing focus, the page changing, the rail
/// switching and the dragged List going cancel. The selected page never changes, and the Lists scroll near the
/// sidebar's edges. The real mouse drag is a FlaUI test (ListsPaneTests).
/// </summary>
[TestClass]
public sealed class ListOwnDragTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_drag_shows_the_Lists_row_at_80_percent_with_the_press_point_under_the_pointer(bool rail)
    {
        using var db = await DatabaseAsync("A", "B");
        var (window, _, lists) = await OpenAsync(db, rail);
        try
        {
            var list = ListsList(window);
            var drag = window.Navigation.ListDrag;
            var a = Row(list, "A");
            var press = new Point(LeftOf(a, list) + 12, TopOf(a, list) + 10);

            await drag.PressAsync(press);
            Assert.IsNull(drag.Picture, "No picture on a press alone.");
            var pointer = new Point(press.X + 5, press.Y + 30);
            Assert.IsTrue(drag.Move(pointer), "Moving past the threshold starts the drag.");

            var picture = drag.Picture;
            Assert.IsNotNull(picture, "The picture shows.");
            Assert.IsTrue(picture.IsOpen);
            Assert.IsFalse(picture.ShouldConstrainToRootBounds, "The picture may leave the window.");
            var image = (Image)picture.Child;
            Assert.AreEqual(0.8, image.Opacity, 0.001);
            Assert.AreEqual(a.ActualWidth, image.Width, 0.5, "The List's row, as wide.");
            Assert.AreEqual(a.ActualHeight, image.Height, 0.5, "The List's row, as tall.");
            AssertPressPointUnderPointer(picture, list, pointer, new Point(12, 10));

            // Far outside the sidebar, outside the window too, the picture keeps following.
            var outside = new Point(press.X + 700, press.Y - 300);
            drag.Move(outside);
            AssertPressPointUnderPointer(picture, list, outside, new Point(12, 10));
            Assert.AreEqual(3, lists.Lists.Count);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_release_over_the_Lists_drops_into_the_gap_saves_the_order_and_keeps_the_selected_page(bool rail)
    {
        using var db = await DatabaseAsync("A", "B", "C");
        var (window, viewModel, lists) = await OpenAsync(db, rail);
        try
        {
            await lists.OpenAsync(new SidebarEntry.ListEntry(lists.Lists.Single(l => l.Name == "A").Id), Ct);
            var list = ListsList(window);
            var drag = window.Navigation.ListDrag;
            var line = Line(window);
            var a = Row(list, "A");

            await drag.PressAsync(new Point(LeftOf(a, list) + 12, Middle(Row(list, "C"), list)));
            var at = new Point(LeftOf(a, list) + 12, TopOf(a, list) + 3);
            drag.Move(at);
            await LineAtAsync(line, list, TopOf(a, list), "Over A's top half");
            Assert.AreEqual("A", lists.SelectedList?.Name, "The drag selects nothing.");
            Assert.AreSame(lists.SelectedList, list.SelectedItem, "The Lists keep their selection while dragged.");

            Assert.IsTrue(drag.TryRelease(at, out var drop), "A release ends a drag.");
            Assert.IsNull(drag.Picture, "The picture goes at the release.");
            await drop;

            Assert.IsFalse(IsShown(line), "The line hides after the drop.");
            await NamesAsync(window, "Inbox", "C", "A", "B");
            CollectionAssert.AreEqual(new[] { "Inbox", "C", "A", "B" }, await StoredOrderAsync(db));
            Assert.AreEqual("A", lists.SelectedList?.Name, "The selected page stays A's.");
            Assert.IsTrue(viewModel.IsListPage);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_insertion_line_follows_the_gap_under_the_pointer_and_hides_outside_the_Lists()
    {
        using var db = await DatabaseAsync("A", "B");
        var (window, _, _) = await OpenAsync(db, rail: false);
        try
        {
            var list = ListsList(window);
            var drag = window.Navigation.ListDrag;
            var line = Line(window);
            var a = Row(list, "A");
            var b = Row(list, "B");

            await drag.PressAsync(new Point(30, Middle(Row(list, "Inbox"), list)));
            drag.Move(new Point(30, Bottom(a, list) - 3));
            await LineAtAsync(line, list, TopOf(b, list), "Over A's bottom half");

            drag.Move(new Point(30, Bottom(b, list) - 3));
            await LineAtAsync(line, list, Bottom(b, list), "Over the last row's bottom half");

            drag.Move(new Point(list.ActualWidth + 30, Middle(a, list)));
            Assert.IsFalse(IsShown(line), "Outside the Lists the line hides.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_release_outside_the_Lists_cancels_and_moves_nothing()
    {
        using var db = await DatabaseAsync("A", "B");
        var (window, _, _) = await OpenAsync(db, rail: false);
        try
        {
            var list = ListsList(window);
            var drag = window.Navigation.ListDrag;

            await drag.PressAsync(new Point(30, Middle(Row(list, "A"), list)));
            drag.Move(new Point(30, Bottom(Row(list, "B"), list) - 3));
            var outside = new Point(list.ActualWidth + 40, Middle(Row(list, "B"), list));
            drag.Move(outside);
            Assert.IsTrue(drag.TryRelease(outside, out var drop), "The release ends the drag.");
            await drop;

            AssertCancelled(window, outside);
            CollectionAssert.AreEqual(new[] { "Inbox", "A", "B" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Esc_cancels_a_List_drag_at_once_and_does_nothing_without_one()
    {
        using var db = await DatabaseAsync("A", "B");
        var (window, _, _) = await OpenAsync(db, rail: false);
        try
        {
            var list = ListsList(window);
            var pane = window.Navigation;
            Assert.IsFalse(pane.TryShortcut(VirtualKey.Escape, VirtualKeyModifiers.None), "Esc does nothing with no drag.");

            var at = await StartDragAsync(window, "A", Bottom(Row(list, "B"), list) - 3);

            Assert.IsTrue(pane.TryShortcut(VirtualKey.Escape, VirtualKeyModifiers.None), "Esc cancels the drag.");
            AssertCancelled(window, at);
            CollectionAssert.AreEqual(new[] { "Inbox", "A", "B" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_press_that_moves_within_the_threshold_is_not_a_drag()
    {
        using var db = await DatabaseAsync("A");
        var (window, _, _) = await OpenAsync(db, rail: false);
        try
        {
            var list = ListsList(window);
            var drag = window.Navigation.ListDrag;
            var press = new Point(30, Middle(Row(list, "A"), list));

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
    public async Task A_touch_or_pen_press_on_a_List_takes_its_rows_menu_away_until_the_press_ends_however_it_ends()
    {
        using var db = await DatabaseAsync("A", "B");
        var (window, _, _) = await OpenAsync(db, rail: false);
        var other = new Window { Title = "Another window" };
        try
        {
            var list = ListsList(window);
            var drag = window.Navigation.ListDrag;
            var a = Row(list, "A");
            var menu = a.ContextFlyout;
            Assert.IsNotNull(menu, "The row has its context menu.");
            var at = new Point(30, Middle(a, list));

            await drag.PressAsync(at);
            Assert.AreSame(menu, a.ContextFlyout, "A mouse press keeps the row's menu.");
            drag.Cancel();

            await drag.PressAsync(at, DragPointer.Touch);
            Assert.IsNull(a.ContextFlyout, "A touch press takes the row's menu away, so its hold opens none.");
            other.Activate(); // the pointer's capture goes with the focus, and its release may land anywhere
            await UiWait.UntilAsync(() => drag.Item is null, () => "Losing focus did not end the press.", Ct);
            Assert.AreSame(menu, a.ContextFlyout, "The window losing focus gives the row its menu back.");

            window.Activate();
            await drag.PressAsync(at, DragPointer.Pen);
            Assert.IsNull(a.ContextFlyout, "A pen press takes the row's menu away.");
            drag.Cancel();
            Assert.AreSame(menu, a.ContextFlyout, "A cancelled press gives the row its menu back.");

            await drag.PressAsync(at, DragPointer.Touch);
            Assert.IsFalse(drag.Move(new Point(30, Bottom(Row(list, "B"), list) - 3)), "A move before the hold is a swipe.");
            Assert.AreSame(menu, a.ContextFlyout, "A swipe gives the row its menu back.");

            await drag.PressAsync(at, DragPointer.Touch);
            Assert.IsFalse(drag.TryRelease(new Point(-50, -50), out _), "A touch press not yet held is no drag.");
            Assert.AreSame(menu, a.ContextFlyout, "A press released anywhere gives the row its menu back.");
        }
        finally
        {
            other.Close();
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_window_losing_focus_cancels_the_List_drag()
    {
        using var db = await DatabaseAsync("A", "B");
        var (window, _, _) = await OpenAsync(db, rail: false);
        var other = new Window { Title = "Another window" };
        try
        {
            var list = ListsList(window);
            var at = await StartDragAsync(window, "A", Bottom(Row(list, "B"), list) - 3);

            other.Activate();

            await UiWait.UntilAsync(() => !window.Navigation.ListDrag.IsDragging,
                () => "Losing focus did not cancel the drag.", Ct);
            AssertCancelled(window, at);
            CollectionAssert.AreEqual(new[] { "Inbox", "A", "B" }, await StoredOrderAsync(db));
        }
        finally
        {
            other.Close();
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Changing_the_page_cancels_the_List_drag_at_once()
    {
        using var db = await DatabaseAsync("A", "B");
        var (window, viewModel, _) = await OpenAsync(db, rail: false);
        try
        {
            var list = ListsList(window);
            var at = await StartDragAsync(window, "A", Bottom(Row(list, "B"), list) - 3);

            var showing = viewModel.ShowSmartViewAsync(SmartView.All, Ct);
            AssertCancelled(window, at);
            await showing;
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Switching_to_the_rail_cancels_the_List_drag()
    {
        using var db = await DatabaseAsync("A", "B");
        var (window, _, _) = await OpenAsync(db, rail: false);
        try
        {
            var list = ListsList(window);
            var at = await StartDragAsync(window, "A", Bottom(Row(list, "B"), list) - 3);

            window.Navigation.SetRail(true);

            AssertCancelled(window, at);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_dragged_List_deleted_cancels_the_drag()
    {
        using var db = await DatabaseAsync("A", "B", "C");
        var (window, _, lists) = await OpenAsync(db, rail: false);
        try
        {
            var list = ListsList(window);
            var at = await StartDragAsync(window, "A", Bottom(Row(list, "C"), list) - 3);

            await db.Get<IListService>().DeleteListAsync(lists.Lists.Single(l => l.Name == "A").Id, Ct);
            await lists.LoadAsync(Ct);

            await UiWait.UntilAsync(() => !window.Navigation.ListDrag.IsDragging,
                () => "The List's going did not cancel the drag.", Ct);
            AssertCancelled(window, at);
            CollectionAssert.AreEqual(new[] { "Inbox", "B", "C" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_reload_that_keeps_the_dragged_List_leaves_the_drag_on_and_the_drop_lands_in_the_gap()
    {
        using var db = await DatabaseAsync("A", "B", "C");
        var (window, _, lists) = await OpenAsync(db, rail: false);
        try
        {
            var list = ListsList(window);
            var drag = window.Navigation.ListDrag;
            await StartDragAsync(window, "A", Bottom(Row(list, "B"), list) - 3);

            // A Task added elsewhere reloads the Lists for their counts: new rows, the same Lists.
            await db.Get<ITaskService>().CreateTaskAsync("Milk", Ct);
            await lists.LoadAsync(Ct);
            await NamesAsync(window, "Inbox", "A", "B", "C");

            Assert.IsTrue(drag.IsDragging, "A reload that keeps the List does not cancel the drag.");
            Assert.IsNotNull(drag.Picture, "The picture still shows.");
            var c = Row(list, "C");
            var at = new Point(30, Bottom(c, list) - 3);
            Assert.IsTrue(drag.Move(at));
            Assert.IsTrue(drag.TryRelease(at, out var drop));
            await drop;

            await NamesAsync(window, "Inbox", "B", "C", "A");
            CollectionAssert.AreEqual(new[] { "Inbox", "B", "C", "A" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    [DataRow(DragPointer.Touch)]
    [DataRow(DragPointer.Pen)]
    public async Task A_touch_or_pen_press_held_then_moved_drags_and_a_swipe_before_the_hold_does_not(DragPointer pointer)
    {
        using var db = await DatabaseAsync("A", "B");
        var (window, _, _) = await OpenAsync(db, rail: false);
        try
        {
            var list = ListsList(window);
            var drag = window.Navigation.ListDrag;
            var clock = new FakeTimeProvider();
            drag.Clock = clock;
            var press = new Point(30, Middle(Row(list, "A"), list));
            var at = new Point(30, Bottom(Row(list, "B"), list) - 3);

            await drag.PressAsync(press, pointer);
            Assert.IsFalse(drag.Move(at), "A move before the hold is a swipe.");
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(100, Ct);
            Assert.IsFalse(drag.IsHeld, "The swipe ended the press.");

            await drag.PressAsync(press, pointer);
            clock.Advance(DragRules.HoldTime);
            await UiWait.UntilAsync(() => drag.IsHeld, () => "The press was never held.", Ct);
            Assert.IsTrue(drag.Move(at), "A move after the hold drags.");
            Assert.IsTrue(drag.TryRelease(at, out var drop));
            await drop;

            CollectionAssert.AreEqual(new[] { "Inbox", "B", "A" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Near_the_sidebars_bottom_edge_the_Lists_scroll_and_the_line_keeps_to_the_gap(bool rail)
    {
        using var db = await DatabaseAsync([.. Enumerable.Range(1, 30).Select(i => $"L{i:00}")]);
        var (window, _, _) = await OpenAsync(db, rail);
        try
        {
            var list = ListsList(window);
            var scroll = SidebarScroll(window);
            await UiWait.UntilAsync(() => scroll.ScrollableHeight > 300,
                () => $"The sidebar does not scroll ({scroll.ScrollableHeight}).", Ct);
            var drag = window.Navigation.ListDrag;
            var line = Line(window);

            // 5 px above the sidebar's visible bottom edge, over a List row: 700 DIPs a second, 175 in a quarter second.
            var bottom = ViewportTopIn(scroll, list) + scroll.ViewportHeight;
            var at = new Point(20, bottom - 5);
            await drag.PressAsync(new Point(20, Middle(Row(list, "L01"), list)));
            drag.Move(at);
            await Task.Delay(100, Ct);
            Assert.AreEqual(0, scroll.VerticalOffset, "Nothing scrolls before time passes.");

            db.Clock.Advance(TimeSpan.FromMilliseconds(250));
            await UiWait.UntilAsync(() => Math.Abs(scroll.VerticalOffset - 175) <= 1,
                () => $"Scrolled to {scroll.VerticalOffset}, expected 175.", Ct);

            // The pointer stays put on the screen, so in the Lists' terms it moved down with the scroll.
            var inList = new Point(20, ViewportTopIn(scroll, list) + scroll.ViewportHeight - 5);
            var under = RowsOf(list).Single(r => TopOf(r, list) <= inList.Y && inList.Y < Bottom(r, list));
            var expected = inList.Y < Middle(under, list) ? TopOf(under, list) : Bottom(under, list);
            await LineAtAsync(line, list, expected, "After the scroll");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_release_over_Lists_scrolled_out_of_the_sidebar_cancels()
    {
        using var db = await DatabaseAsync([.. Enumerable.Range(1, 30).Select(i => $"L{i:00}")]);
        var (window, _, _) = await OpenAsync(db, rail: false);
        try
        {
            var list = ListsList(window);
            var scroll = SidebarScroll(window);
            await UiWait.UntilAsync(() => scroll.ScrollableHeight > 300,
                () => $"The sidebar does not scroll ({scroll.ScrollableHeight}).", Ct);
            scroll.ChangeView(null, 300, null, true);
            await UiWait.UntilAsync(() => Math.Abs(scroll.VerticalOffset - 300) <= 1, () => "Did not scroll.", Ct);

            var drag = window.Navigation.ListDrag;
            var top = ViewportTopIn(scroll, list);
            var visible = RowsOf(list).First(r => TopOf(r, list) >= top + 50);
            await drag.PressAsync(new Point(20, Middle(visible, list)));

            // Above the sidebar's visible top, where Lists scrolled out of sight still lie under the pointer.
            var hidden = new Point(20, top - 20);
            Assert.IsTrue(hidden.Y > 0, "The point is over the Lists' own bounds.");
            drag.Move(hidden);
            Assert.IsFalse(IsShown(Line(window)), "No line over Lists out of sight.");
            Assert.IsTrue(drag.TryRelease(hidden, out var drop));
            await drop;

            CollectionAssert.AreEqual(
                new[] { "Inbox" }.Concat(Enumerable.Range(1, 30).Select(i => $"L{i:00}")).ToArray(),
                await StoredOrderAsync(db));
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
        using var db = await DatabaseAsync("A");
        var (window, _, _) = await OpenAsync(db, rail: false);
        try
        {
            ((FrameworkElement)window.Content).RequestedTheme = theme;
            var list = ListsList(window);
            var drag = window.Navigation.ListDrag;
            var press = new Point(30, Middle(Row(list, "A"), list));

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

    // Presses the List named, moves to y over the Lists, and checks the drag is on with its picture and line.
    private static async Task<Point> StartDragAsync(MainWindow window, string name, double y)
    {
        var list = ListsList(window);
        var drag = window.Navigation.ListDrag;
        await drag.PressAsync(new Point(30, Middle(Row(list, name), list)));
        var at = new Point(30, y);
        Assert.IsTrue(drag.Move(at), "The drag started.");
        Assert.IsNotNull(drag.Picture, "The picture shows.");
        Assert.IsTrue(IsShown(Line(window)), "The line shows.");
        return at;
    }

    // The picture and the line are gone, and the pointer neither drags nor drops any more.
    private static void AssertCancelled(MainWindow window, Point at)
    {
        var drag = window.Navigation.ListDrag;
        Assert.IsFalse(drag.IsDragging, "The drag is off.");
        Assert.IsNull(drag.Picture, "The picture is gone.");
        Assert.IsFalse(IsShown(Line(window)), "The line is gone.");
        Assert.IsFalse(drag.Move(at), "The pointer no longer drags.");
        Assert.IsFalse(drag.TryRelease(at, out _), "The release is no drop.");
    }

    // The picture's press point (its offset into the picture) sits under the pointer, in window coordinates.
    private static void AssertPressPointUnderPointer(Popup picture, ListView list, Point pointer, Point pressOffset)
    {
        var inWindow = list.TransformToVisual(null).TransformPoint(pointer);
        Assert.AreEqual(inWindow.X, picture.HorizontalOffset + pressOffset.X, 0.5, "Press point x.");
        Assert.AreEqual(inWindow.Y, picture.VerticalOffset + pressOffset.Y, 0.5, "Press point y.");
    }

    private async Task LineAtAsync(Rectangle line, ListView list, double expected, string where) =>
        await UiWait.UntilAsync(
            () => IsShown(line) && Math.Abs(TopOf(line, list) + (line.ActualHeight / 2) - expected) <= 1.5,
            () => $"{where}: line shown {IsShown(line)}, at {TopOf(line, list) + (line.ActualHeight / 2)}, expected {expected}.",
            Ct);

    private async Task NamesAsync(MainWindow window, params string[] names)
    {
        var list = ListsList(window);
        await UiWait.UntilAsync(
            () => RowsOf(list).Select(AutomationProperties.GetName).SequenceEqual(names) && RowsOf(list).All(r => r.IsLoaded),
            () => $"Lists were [{string.Join(", ", RowsOf(list).Select(AutomationProperties.GetName))}].", Ct);
    }

    // The sidebar's visible top in the Lists' coordinates.
    private static double ViewportTopIn(ScrollViewer scroll, UIElement list) =>
        scroll.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y;

    private static ListView ListsList(MainWindow window) => VisualTree.Find<ListView>(window.Navigation, "ListsList");

    private static ScrollViewer SidebarScroll(MainWindow window) =>
        VisualTree.Find<ScrollViewer>(window.Navigation, "SidebarScroll");

    private static Rectangle Line(MainWindow window) => VisualTree.Find<Rectangle>(window.Navigation, "ListInsertionLine");

    private static List<ListViewItem> RowsOf(ListView list) =>
        [.. Enumerable.Range(0, list.Items.Count).Select(list.ContainerFromIndex).OfType<ListViewItem>()];

    private static ListViewItem Row(ListView list, string name) =>
        RowsOf(list).Single(r => AutomationProperties.GetName(r) == name);

    // Shown: it and every ancestor are visible.
    private static bool IsShown(DependencyObject element)
    {
        for (var current = element; current is not null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: Visibility.Collapsed })
            {
                return false;
            }
        }

        return true;
    }

    private static double TopOf(FrameworkElement element, UIElement list) =>
        element.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y;

    private static double LeftOf(FrameworkElement element, UIElement list) =>
        element.TransformToVisual(list).TransformPoint(new Point(0, 0)).X;

    private static double Middle(FrameworkElement row, UIElement list) => TopOf(row, list) + (row.ActualHeight / 2);

    private static double Bottom(FrameworkElement row, UIElement list) => TopOf(row, list) + row.ActualHeight;

    private static async Task<string[]> StoredOrderAsync(TempDatabase db) =>
        [.. (await db.Get<IListService>().GetListsAsync()).Select(l => l.List.Name)];

    // A window over the database with its Lists loaded and shown, in the full sidebar or the rail.
    private async Task<(MainWindow Window, MainViewModel ViewModel, SidebarViewModel Lists)> OpenAsync(
        TempDatabase db, bool rail)
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
            var list = ListsList(window);
            await UiWait.UntilAsync(
                () => RowsOf(list).Count == lists.Lists.Count && RowsOf(list).All(r => r.IsLoaded && r.ActualHeight > 0),
                () => "The Lists did not show.", Ct);
            return (window, viewModel, lists);
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    // The Default List (Inbox) and these Lists, in this order.
    private async Task<TempDatabase> DatabaseAsync(params string[] names)
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
