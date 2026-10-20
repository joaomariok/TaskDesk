using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

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
        AppSession.DragSlowly(Centre(Row("First")), Centre(Row("Third")));

        _app.WaitForRows(Ids.OpenTaskList, "Second", "Third", "First");

        _app.Restart();

        _app.WaitForRows(Ids.OpenTaskList, "Second", "Third", "First");
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
}
