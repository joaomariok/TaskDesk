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
using Windows.Foundation;

namespace TaskDesk.Views.Tests;

/// <summary>
/// Touch and pen start the app's own Task drag by press-and-hold (#136), driven through the drag controller with
/// points and a fake clock: a press held for Windows' hold time then moved drags and drops; a move before the hold
/// is a swipe that never drags. The real touch drag is a FlaUI test (ReorderTests).
/// </summary>
[TestClass]
public sealed class TaskTouchDragTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    [DataRow(DragPointer.Touch)]
    [DataRow(DragPointer.Pen)]
    public async Task A_press_held_for_the_hold_time_then_moved_drags_and_drops(DragPointer pointer)
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
            var clock = new FakeTimeProvider();
            drag.Clock = clock;
            var c = Container(list, "C");

            await drag.PressAsync(new Point(30, Middle(Container(list, "A"), list)), pointer);
            clock.Advance(DragRules.HoldTime);
            await UiWait.UntilAsync(() => drag.IsHeld, () => "The press was never held.", Ct);
            var at = new Point(30, TopOf(c, list) + c.ActualHeight - 3);
            Assert.IsTrue(drag.Move(at), "A move after the hold starts the drag.");
            Assert.IsNotNull(drag.Picture, "The picture shows.");
            Assert.IsTrue(drag.TryRelease(at, out var drop));
            await drop;

            await WaitForRowsAsync(window, "B", "C", "A");
            CollectionAssert.AreEqual(new[] { "B", "C", "A" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    [DataRow(DragPointer.Touch)]
    [DataRow(DragPointer.Pen)]
    public async Task A_swipe_before_the_hold_never_drags_even_once_the_hold_time_passes(DragPointer pointer)
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
            var clock = new FakeTimeProvider();
            drag.Clock = clock;
            var press = new Point(30, Middle(Container(list, "A"), list));

            await drag.PressAsync(press, pointer);
            clock.Advance(DragRules.HoldTime - TimeSpan.FromMilliseconds(1));
            var at = new Point(30, Middle(Container(list, "B"), list));
            Assert.IsFalse(drag.Move(at), "A move before the hold does not drag.");
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(100, Ct);
            Assert.IsFalse(drag.IsHeld, "The swipe ended the press, so it is never held.");
            Assert.IsFalse(drag.Move(new Point(30, at.Y + 10)), "Nor does any later move.");
            Assert.IsNull(drag.Picture);
            Assert.IsFalse(drag.TryRelease(at, out _), "The release ends no drag.");
            CollectionAssert.AreEqual(new[] { "A", "B" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_mouse_press_still_drags_past_the_threshold_without_a_hold()
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
            drag.Clock = new FakeTimeProvider();

            await drag.PressAsync(new Point(30, Middle(Container(list, "A"), list)), DragPointer.Mouse);
            Assert.IsTrue(drag.Move(new Point(30, Middle(Container(list, "B"), list))));
            drag.Cancel();
        }
        finally
        {
            window.Close();
        }
    }

    private static double TopOf(FrameworkElement element, UIElement list) =>
        element.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y;

    private static double Middle(FrameworkElement row, UIElement list) => TopOf(row, list) + (row.ActualHeight / 2);

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

    private async Task AddAsync(TempDatabase db, string title)
    {
        await db.Get<ITaskService>().CreateTaskAsync(title, Ct);
    }
}
