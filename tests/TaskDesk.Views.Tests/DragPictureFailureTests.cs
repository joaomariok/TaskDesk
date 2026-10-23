using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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
/// A press whose row leaves the list while the drag picture is being taken (a reload, a Task saved from quick add,
/// a toast's Mark done) simply ends: nothing is thrown, no drag starts, no picture shows, the order is unchanged,
/// and the next press drags as usual. Both drags share one controller, so the Task list stands for both.
/// </summary>
[TestClass]
public sealed class DragPictureFailureTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task A_row_removed_during_the_press_ends_it_and_the_next_press_drags()
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
            await UiWait.UntilAsync(() => Shown(list).SequenceEqual(["A", "B", "C"]),
                () => $"Rows were [{string.Join(", ", Shown(list))}].", Ct);
            var drag = window.View.TaskList.TaskDrag;

            // The press has started and is waiting for its picture when the row leaves the list.
            var pressing = drag.PressAsync(new Point(30, Middle(Container(list, "A"), list)));
            viewModel.Tasks.Remove(viewModel.Tasks.Single(t => t.Title == "A"));
            await pressing;

            Assert.IsFalse(drag.IsDragging, "No drag started.");
            Assert.IsNull(drag.Picture, "No picture shows.");
            Assert.IsFalse(drag.Move(new Point(30, 5)), "The pointer drags nothing.");
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, await StoredOrderAsync(db));

            await UiWait.UntilAsync(() => Shown(list).SequenceEqual(["B", "C"]),
                () => $"Rows were [{string.Join(", ", Shown(list))}].", Ct);
            await drag.PressAsync(new Point(30, Middle(Container(list, "B"), list)));
            Assert.IsTrue(drag.Move(new Point(30, Middle(Container(list, "B"), list) + 8)), "The next press drags.");
            Assert.IsNotNull(drag.Picture, "The next press shows its picture.");
            drag.Cancel();
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_picture_that_fails_with_a_non_COM_exception_ends_the_press_quietly()
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
            await UiWait.UntilAsync(() => Shown(list).SequenceEqual(["A", "B", "C"]),
                () => $"Rows were [{string.Join(", ", Shown(list))}].", Ct);

            // The host's backing colour is read while the picture is composed, after the rows rendered. It fails the
            // way a RenderAsync can (E_INVALIDARG surfaces as ArgumentException), which is not a COMException.
            var drag = new ReorderDrag(new FailingPictureHost((IReorderDragHost)window.View.TaskList), viewModel);
            var middle = Middle(Container(list, "A"), list);

            await drag.PressAsync(new Point(30, middle));

            Assert.IsFalse(drag.IsDragging, "No drag started.");
            Assert.IsNull(drag.Picture, "No picture shows.");
            Assert.IsNull(drag.Item, "The press ended.");
            Assert.IsFalse(drag.Move(new Point(30, middle + 8)), "The pointer drags nothing.");
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    // Delegates to the Task list, but its picture cannot be composed.
    private sealed class FailingPictureHost(IReorderDragHost inner) : IReorderDragHost
    {
        public FrameworkElement Surface => inner.Surface;

        public FrameworkElement Viewport => inner.Viewport;

        public Windows.UI.Color PictureBacking => throw new ArgumentException("The picture cannot be composed.");

        public DragSubject? SubjectAt(Point at) => inner.SubjectAt(at);

        public Microsoft.UI.Xaml.Media.Brush LineFill => inner.LineFill;

        public Rect? ShowDragAt(Point at) => inner.ShowDragAt(at);

        public void HideDragLine() => inner.HideDragLine();

        public Task DropAsync(object item, Point at) => inner.DropAsync(item, at);

        public bool ScrollBy(double dy) => inner.ScrollBy(dy);
    }

    private static double Middle(FrameworkElement row, UIElement list) =>
        row.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y + (row.ActualHeight / 2);

    private static IEnumerable<ListViewItem> ShownRows(ListView list) =>
        VisualTree.Descendants(list).OfType<ListViewItem>()
            .Where(c => list.IndexFromContainer(c) >= 0)
            .OrderBy(list.IndexFromContainer);

    private static string[] Shown(ListView list) =>
        [.. ShownRows(list).Where(c => c.ActualHeight > 0).Select(AutomationProperties.GetName)];

    private static ListViewItem Container(ListView list, string name) =>
        ShownRows(list).Single(c => AutomationProperties.GetName(c) == name);

    private static async Task<string[]> StoredOrderAsync(TempDatabase db) =>
        [.. (await db.QueryAsync(c => c.Tasks.Where(t => !t.IsCompleted).OrderBy(t => t.SortOrder))).Select(t => t.Title)];
}
