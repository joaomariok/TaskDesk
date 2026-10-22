using Microsoft.Extensions.Time.Testing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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
/// The window's part in the Task and List drags: Esc cancels either wherever the focus is (the real key's routing is
/// the UI tests', DragInputTests), closing the window cancels both, and their hold and edge-scroll timers run
/// through the view model's lifetime (docs/agents/winui.md, Lifetime), so after its stop neither scrolls a list nor
/// holds a press.
/// </summary>
[TestClass]
public sealed class WindowDragTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task Esc_on_the_window_cancels_a_Task_drag_and_a_List_drag_and_does_nothing_without_one()
    {
        using var db = await DatabaseAsync(taskCount: 3, "A", "B");
        var (window, _) = await OpenAsync(db);
        try
        {
            Assert.IsFalse(window.TryShortcut(VirtualKey.Escape, VirtualKeyModifiers.None), "Esc does nothing with no drag.");

            var tasks = VisualTree.Find<ListView>(window.View, "OpenTaskList");
            var taskDrag = window.View.TaskList.TaskDrag;
            await taskDrag.PressAsync(new Point(30, Middle(Row(tasks, "T01"), tasks)));
            Assert.IsTrue(taskDrag.Move(new Point(30, Middle(Row(tasks, "T03"), tasks))));
            Assert.IsTrue(window.TryShortcut(VirtualKey.Escape, VirtualKeyModifiers.None), "Esc cancels the Task drag.");
            Assert.IsFalse(taskDrag.IsDragging);
            Assert.IsNull(taskDrag.Picture);

            var lists = ListsList(window);
            var listDrag = window.Navigation.ListDrag;
            await listDrag.PressAsync(new Point(30, Middle(Row(lists, "A"), lists)));
            Assert.IsTrue(listDrag.Move(new Point(30, Middle(Row(lists, "B"), lists))));
            Assert.IsTrue(window.TryShortcut(VirtualKey.Escape, VirtualKeyModifiers.None), "Esc cancels the List drag.");
            Assert.IsFalse(listDrag.IsDragging);
            Assert.IsNull(listDrag.Picture);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Closing_the_window_cancels_a_Task_drag_and_a_List_drag()
    {
        using var db = await DatabaseAsync(taskCount: 3, "A", "B");
        var (window, _) = await OpenAsync(db);
        var closed = false;
        try
        {
            var tasks = VisualTree.Find<ListView>(window.View, "OpenTaskList");
            var taskDrag = window.View.TaskList.TaskDrag;
            await taskDrag.PressAsync(new Point(30, Middle(Row(tasks, "T01"), tasks)));
            Assert.IsTrue(taskDrag.Move(new Point(30, Middle(Row(tasks, "T03"), tasks))), "The Task drag starts.");

            var lists = ListsList(window);
            var listDrag = window.Navigation.ListDrag;
            await listDrag.PressAsync(new Point(30, Middle(Row(lists, "A"), lists)));
            Assert.IsTrue(listDrag.Move(new Point(30, Middle(Row(lists, "B"), lists))), "The List drag starts.");

            window.Close();
            closed = true;

            Assert.IsFalse(taskDrag.IsDragging, "Closing the window cancels the Task drag.");
            Assert.IsNull(taskDrag.Picture, "The Task drag's picture goes.");
            Assert.IsFalse(listDrag.IsDragging, "Closing the window cancels the List drag.");
            Assert.IsNull(listDrag.Picture, "The List drag's picture goes.");
        }
        finally
        {
            if (!closed)
            {
                window.Close();
            }
        }
    }

    [UITestMethod]
    public async Task After_the_view_model_stops_a_drag_neither_scrolls_at_the_edge_nor_holds_a_touch_press()
    {
        using var db = await DatabaseAsync(taskCount: 50);
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
            var scroll = VisualTree.Descendants(list).OfType<ScrollViewer>().First();
            await UiWait.UntilAsync(() => scroll.ScrollableHeight > 300, () => "The list did not overflow.", Ct);
            var drag = window.View.TaskList.TaskDrag;
            await drag.PressAsync(new Point(30, Middle(Row(list, "T01"), list)));
            Assert.IsTrue(drag.Move(new Point(30, list.ActualHeight - 5)), "The drag reaches the bottom edge band.");

            await viewModel.StopAsync();
            db.Clock.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(100, Ct);
            Assert.AreEqual(0, scroll.VerticalOffset, "After the stop the edge timer scrolls nothing.");

            drag.Cancel();
            var clock = new FakeTimeProvider();
            drag.Clock = clock;
            await drag.PressAsync(new Point(30, Middle(Row(list, "T01"), list)), DragPointer.Touch);
            clock.Advance(DragRules.HoldTime + TimeSpan.FromMilliseconds(100));
            await Task.Delay(100, Ct);
            Assert.IsFalse(drag.IsHeld, "After the stop a touch press is never held.");
        }
        finally
        {
            window.Close();
        }
    }

    private static double Middle(FrameworkElement row, UIElement list) =>
        row.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y + (row.ActualHeight / 2);

    private static ListViewItem Row(ListView list, string name) =>
        VisualTree.Descendants(list).OfType<ListViewItem>()
            .Single(c => list.IndexFromContainer(c) >= 0 && AutomationProperties.GetName(c) == name);

    private static ListView ListsList(MainWindow window) =>
        VisualTree.Descendants(window.Navigation).OfType<ListView>().Single(l => l.Name == "ListsList");

    // A window over the database with its Tasks and Lists loaded and their rows shown.
    private async Task<(MainWindow Window, MainViewModel ViewModel)> OpenAsync(TempDatabase db)
    {
        MainWindow? window = null;
        var viewModel = new MainViewModel(db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, db.Get<IDueService>(),
            db.Get<ITaskSearchService>(), listService: db.Get<IListService>(), taskCreator: db.Get<ITaskCreator>());
        var lists = new SidebarViewModel(viewModel, db.Get<IListService>(), new WinUiDialogs(new DialogService(() => window)));
        window = new MainWindow(viewModel, lists);
        window.Activate();
        try
        {
            await viewModel.LoadAsync(Ct);
            await lists.LoadAsync(Ct);
            var tasks = VisualTree.Find<ListView>(window.View, "OpenTaskList");
            await UiWait.UntilAsync(
                () => VisualTree.Descendants(tasks).OfType<ListViewItem>()
                    .Any(c => AutomationProperties.GetName(c) == "T01" && c.ActualHeight > 0),
                () => "The Tasks did not show.", Ct);
            await UiWait.UntilAsync(
                () => VisualTree.Descendants(window.Navigation).OfType<ListView>().Any(l => l.Name == "ListsList")
                      && VisualTree.Descendants(ListsList(window)).OfType<ListViewItem>()
                          .Count(c => c.ActualHeight > 0) == lists.Lists.Count,
                () => "The Lists did not show.", Ct);
            return (window, viewModel);
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    // T01, T02, ... in the Default List, and these Lists after it.
    private async Task<TempDatabase> DatabaseAsync(int taskCount, params string[] lists)
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        for (var i = 1; i <= taskCount; i++)
        {
            await db.Get<ITaskService>().CreateTaskAsync($"T{i:00}", Ct);
        }

        foreach (var name in lists)
        {
            await db.Get<IListService>().CreateListAsync(name, Ct);
        }

        return db;
    }
}
