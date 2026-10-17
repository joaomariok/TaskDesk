using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;

namespace TaskDesk.UiTests;

/// <summary>Dragging a Task row with the real mouse reorders the List and the order survives a restart.</summary>
public sealed class ReorderTests : IDisposable
{
    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Dragging_a_row_reorders_the_List_and_the_order_survives_a_restart()
    {
        string[] titles = ["First", "Second", "Third"];
        for (var i = 0; i < titles.Length; i++)
        {
            _app.TypeInto(Ids.QuickAddBox, titles[i], pressEnter: true);
            _app.WaitForRows(Ids.OpenTaskList, titles[..(i + 1)]);
        }

        // Drag "First" onto "Third" in small steps, as a person would.
        var from = Centre(Row("First"));
        var to = Centre(Row("Third"));
        Mouse.Position = from;
        Mouse.Down(MouseButton.Left);
        for (var step = 1; step <= 10; step++)
        {
            Mouse.Position = new Point(from.X + ((to.X - from.X) * step / 10), from.Y + ((to.Y - from.Y) * step / 10));
            Thread.Sleep(30);
        }

        Mouse.Up(MouseButton.Left);

        _app.WaitForRows(Ids.OpenTaskList, "Second", "Third", "First");

        _app.Restart();

        _app.WaitForRows(Ids.OpenTaskList, "Second", "Third", "First");
    }

    private AutomationElement Row(string title) =>
        AppSession.Eventually(
            () => _app.Find(Ids.OpenTaskList).FindFirstChild(cf => cf.ByControlType(ControlType.ListItem).And(cf.ByName(title))),
            $"No row '{title}'.");

    private static Point Centre(AutomationElement element)
    {
        var bounds = element.BoundingRectangle;
        return new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
    }
}
