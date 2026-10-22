using System.Drawing;

namespace TaskDesk.UiTests;

/// <summary>
/// Holding a dragged Task near the bottom edge of the Task list scrolls it with the real mouse and the drag's own
/// timer, so the drop lands in a gap that was below the list when the drag began.
/// </summary>
public sealed class ReorderEdgeScrollTests : IDisposable
{
    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Holding_a_drag_at_the_lists_bottom_edge_scrolls_it_and_the_Task_lands_further_down()
    {
        var titles = Enumerable.Range(1, 14).Select(i => $"T{i:00}").ToArray();
        for (var i = 0; i < titles.Length; i++)
        {
            _app.TypeInto(Ids.QuickAddBox, titles[i], pressEnter: true);
            _app.WaitForRows(Ids.OpenTaskList, titles[..(i + 1)]);
        }

        _app.ResizeTo(900, 480);
        var list = _app.Find(Ids.OpenTaskList).BoundingRectangle;
        var release = new Point(list.Left + (list.Width / 2), list.Bottom - 3);
        var under = titles.First(t => _app.FindRow(Ids.OpenTaskList, t)?.BoundingRectangle is { } r
                                      && r.Top <= release.Y && release.Y < r.Bottom);

        // Without scrolling, the drop would land next to the row under the pointer at the start.
        AppSession.DragSlowly(Centre("T01"), release);

        // UI Automation sees only the rows the list has realized, and the scrolled short list leaves some out: make
        // the window tall enough for every row before reading the order.
        _app.ResizeTo(900, 800);
        AppSession.WaitUntil(
            () => _app.Rows(Ids.OpenTaskList) is var rows && rows.Count == titles.Length && rows[0] != "T01",
            () => "T01 did not move.");
        var order = _app.Rows(Ids.OpenTaskList).ToList();
        Assert.True(
            order.IndexOf("T01") > Array.IndexOf(titles, under) + 1,
            $"T01 landed at {order.IndexOf("T01")} in [{string.Join(", ", order)}]; '{under}' was under the pointer.");
    }

    private Point Centre(string title)
    {
        var bounds = AppSession.Eventually(() => _app.FindRow(Ids.OpenTaskList, title), $"No row '{title}'.")
            .BoundingRectangle;
        return new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
    }
}
