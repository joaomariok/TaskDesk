using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>
/// A row the app's own drag (ADR 0007) moved or let go of keeps no pointer-over background once the drag ends: the
/// list holds the pointer during the drag, so the row never sees the pointer leave it. Read from the screen, in
/// Light: the dragged row's background matches a row the pointer never touched.
/// </summary>
public sealed class DragHoverTests : IDisposable
{
    // Light's pointer-over fill (SubtleFillColorSecondary, 3.5 % black) darkens a row by about 8 per channel.
    private const int MaxBackgroundDifference = 3;

    private readonly AppSession _app = AppSession.Start(theme: "Light");

    public void Dispose() => _app.Dispose();

    [Fact]
    public void A_cancelled_Task_drag_leaves_the_dragged_row_without_its_pointer_over_background()
    {
        foreach (var title in new[] { "First", "Second", "Third" })
        {
            _app.TypeInto(Ids.QuickAddBox, title, pressEnter: true);
        }

        _app.WaitForRows(Ids.OpenTaskList, "First", "Second", "Third");
        var first = AppSession.SettledBounds(() => TaskRow("First"));
        var third = AppSession.SettledBounds(() => TaskRow("Third"));

        AppSession.DragSlowly(Centre(first), Centre(third), beforeRelease: () => AppSession.Press(VirtualKeyShort.ESCAPE));
        Thread.Sleep(500);

        AssertSameBackground(first, AppSession.SettledBounds(() => TaskRow("Second")), "First");
    }

    [Fact]
    public void A_cancelled_List_drag_leaves_the_dragged_row_without_its_pointer_over_background()
    {
        foreach (var name in new[] { "A", "B", "C" })
        {
            _app.Find(Ids.NewListButton).AsButton().Invoke();
            _app.TypeInto(Ids.NewListBox, name, pressEnter: true);
            AppSession.WaitUntil(() => _app.Rows(Ids.ListsList).Contains(name),
                () => $"Lists were [{string.Join(", ", _app.Rows(Ids.ListsList))}].");
        }

        var a = AppSession.SettledBounds(() => ListRow("A"));
        var c = AppSession.SettledBounds(() => ListRow("C"));

        AppSession.DragSlowly(Centre(c), Centre(a), beforeRelease: () => AppSession.Press(VirtualKeyShort.ESCAPE));
        Thread.Sleep(500);

        AssertSameBackground(c, AppSession.SettledBounds(() => ListRow("B")), "C");
    }
    // Compares one pixel of each row's empty right-hand part, at the same place in each.
    private static void AssertSameBackground(Rectangle dragged, Rectangle untouched, string name)
    {
        var draggedColour = PixelAt(dragged);
        var untouchedColour = PixelAt(untouched);
        var difference = Math.Max(
            Math.Abs(draggedColour.R - untouchedColour.R),
            Math.Max(Math.Abs(draggedColour.G - untouchedColour.G), Math.Abs(draggedColour.B - untouchedColour.B)));
        Assert.True(
            difference <= MaxBackgroundDifference,
            $"'{name}' kept a pointer-over background: {draggedColour} against {untouchedColour} on a row the pointer never touched.");
    }

    private static Color PixelAt(Rectangle row)
    {
        using var image = FlaUI.Core.Capturing.Capture.Rectangle(
            new Rectangle(row.Left + (row.Width * 3 / 4), row.Top + (row.Height / 2), 1, 1));
        return image.Bitmap.GetPixel(0, 0);
    }

    private AutomationElement TaskRow(string title) =>
        AppSession.Eventually(() => _app.FindRow(Ids.OpenTaskList, title), $"No row '{title}'.");

    private AutomationElement ListRow(string name) =>
        AppSession.Eventually(
            () => _app.Find(Ids.ListsList).FindFirstChild(cf => cf.ByControlType(ControlType.ListItem).And(cf.ByName(name))),
            $"No List row '{name}'.");

    private static Point Centre(Rectangle bounds) => new(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
}
