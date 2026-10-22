using System.Drawing;
using FlaUI.Core.AutomationElements;

namespace TaskDesk.UiTests;

/// <summary>
/// Reordering Tasks with a real (injected) finger: a press-and-hold then a slide reorders the List and the order
/// survives a restart; a quick swipe never drags.
/// </summary>
public sealed class TouchReorderTests : IDisposable
{
    // Past Windows' 500 ms press-and-hold (DragRules.HoldTime), with room for the app to see it.
    private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(900);

    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void A_press_and_hold_then_a_slide_reorders_the_List_and_the_order_survives_a_restart()
    {
        AddTasks("First", "Second", "Third");

        TouchInput.HoldAndDrag(Centre(Row("First")), BottomQuarter(Row("Third")), Hold);

        _app.WaitForRows(Ids.OpenTaskList, "Second", "Third", "First");

        _app.Restart();

        _app.WaitForRows(Ids.OpenTaskList, "Second", "Third", "First");
    }

    [Fact]
    public void A_quick_swipe_never_drags_a_row()
    {
        AddTasks("First", "Second", "Third");

        TouchInput.HoldAndDrag(Centre(Row("First")), BottomQuarter(Row("Third")), TimeSpan.Zero);

        Thread.Sleep(500);
        _app.WaitForRows(Ids.OpenTaskList, "First", "Second", "Third");
    }

    private void AddTasks(params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++)
        {
            _app.TypeInto(Ids.QuickAddBox, titles[i], pressEnter: true);
            _app.WaitForRows(Ids.OpenTaskList, titles[..(i + 1)]);
        }
    }

    private AutomationElement Row(string title) =>
        AppSession.Eventually(
            () => _app.FindRow(Ids.OpenTaskList, title),
            $"No row '{title}'.");

    private static Point Centre(AutomationElement element)
    {
        var bounds = element.BoundingRectangle;
        return new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
    }

    // The bottom half of a row: a drop there lands after it.
    private static Point BottomQuarter(AutomationElement element)
    {
        var bounds = element.BoundingRectangle;
        return new Point(bounds.Left + (bounds.Width / 2), bounds.Bottom - (bounds.Height / 4));
    }
}
