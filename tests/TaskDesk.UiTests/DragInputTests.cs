using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>
/// The app's own drag (ADR 0007) with real input routed as Windows delivers it: Esc cancels a Task or List drag
/// wherever the focus is; a second finger neither ends nor steers a touch drag, nor ticks a row's checkbox; a touch drag scrolls the Task list at
/// its edge without being cancelled; and a touch hold on a List drags it without its context menu, while a hold
/// that does not slide still opens the menu.
/// </summary>
public sealed class DragInputTests : IDisposable
{
    // Past Windows' 500 ms press-and-hold (DragRules.HoldTime), with room for the app to see it.
    private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(900);

    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Esc_during_a_Task_drag_cancels_it_while_quick_add_has_the_focus()
    {
        AddTasks("First", "Second", "Third");
        Assert.Equal(Ids.QuickAddBox, _app.FocusedAutomationId());

        AppSession.DragSlowly(Centre(TaskRow("First")), BottomQuarter(TaskRow("Third")),
            beforeRelease: () => AppSession.Press(VirtualKeyShort.ESCAPE));
        Thread.Sleep(500);

        Assert.Equal(["First", "Second", "Third"], _app.Rows(Ids.OpenTaskList));
    }

    [Theory]
    [InlineData(Ids.QuickAddBox)]
    [InlineData(Ids.ListsList)]
    public void Esc_during_a_List_drag_cancels_it_wherever_the_focus_is(string focus)
    {
        foreach (var name in new[] { "A", "B", "C" })
        {
            AddList(name);
        }

        if (focus == Ids.ListsList)
        {
            _app.SelectRow(Ids.ListsList, "C");
        }
        else
        {
            _app.Focus(Ids.QuickAddBox);
        }

        var c = AppSession.SettledBounds(() => ListRow("C"));
        var a = AppSession.SettledBounds(() => ListRow("A"));
        AppSession.DragSlowly(
            new Point(c.Left + c.Width / 2, c.Top + c.Height / 2),
            new Point(a.Left + a.Width / 2, a.Top + a.Height / 4),
            beforeRelease: () => AppSession.Press(VirtualKeyShort.ESCAPE));
        Thread.Sleep(500);

        Assert.Equal(["Inbox", "A", "B", "C"], _app.Rows(Ids.ListsList));
    }

    [Fact]
    public void A_second_finger_tapping_the_list_during_a_touch_drag_neither_ends_nor_steers_it()
    {
        AddTasks("First", "Second", "Third");
        var list = _app.Find(Ids.OpenTaskList).BoundingRectangle;

        // The second finger taps the list's empty space below its rows.
        TouchInput.HoldAndDrag(Centre(TaskRow("First")), BottomQuarter(TaskRow("Third")), Hold,
            secondFingerTap: new Point(list.Left + (list.Width / 2), list.Bottom - 40));

        _app.WaitForRows(Ids.OpenTaskList, "Second", "Third", "First");
    }

    [Fact]
    public void A_second_finger_tapping_another_rows_checkbox_during_a_touch_drag_completes_nothing_and_the_drag_lands()
    {
        AddTasks("First", "Second", "Third");
        var check = AppSession.Eventually(
            () => TaskRow("Second").FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox)),
            "No checkbox on 'Second'.");

        TouchInput.HoldAndDrag(Centre(TaskRow("First")), BottomQuarter(TaskRow("Third")), Hold,
            secondFingerTap: Centre(check));

        _app.WaitForRows(Ids.OpenTaskList, "Second", "Third", "First");
        Thread.Sleep(1000); // past the completion hold: a completed Task would have left the open rows by now
        Assert.Equal(["Second", "Third", "First"], _app.Rows(Ids.OpenTaskList));
    }

    [Fact]
    public void A_touch_drag_held_at_the_lists_bottom_edge_scrolls_it_and_the_Task_lands_further_down()
    {
        var titles = Enumerable.Range(1, 14).Select(i => $"T{i:00}").ToArray();
        AddTasks(titles);

        _app.ResizeTo(900, 480);
        var list = _app.Find(Ids.OpenTaskList).BoundingRectangle;
        var release = new Point(list.Left + (list.Width / 2), list.Bottom - 3);
        var under = titles.First(t => _app.FindRow(Ids.OpenTaskList, t)?.BoundingRectangle is { } r
                                      && r.Top <= release.Y && release.Y < r.Bottom);

        TouchInput.HoldAndDrag(Centre(TaskRow("T01")), release, Hold, rest: TimeSpan.FromMilliseconds(600));

        // UI Automation sees only the rows the list has realized: make the window tall enough for every row.
        _app.ResizeTo(900, 800);
        AppSession.WaitUntil(
            () => _app.Rows(Ids.OpenTaskList) is var rows && rows.Count == titles.Length && rows[0] != "T01",
            () => $"T01 did not move: [{string.Join(", ", _app.Rows(Ids.OpenTaskList))}].");
        var order = _app.Rows(Ids.OpenTaskList).ToList();
        Assert.True(
            order.IndexOf("T01") > Array.IndexOf(titles, under) + 1,
            $"T01 landed at {order.IndexOf("T01")} in [{string.Join(", ", order)}]; '{under}' was under the finger.");
    }

    [Fact]
    public void A_touch_hold_then_a_slide_on_a_List_drags_it_without_opening_its_context_menu()
    {
        foreach (var name in new[] { "A", "B", "C" })
        {
            AddList(name);
        }

        var c = AppSession.SettledBounds(() => ListRow("C"));
        var a = AppSession.SettledBounds(() => ListRow("A"));
        TouchInput.HoldAndDrag(
            new Point(c.Left + c.Width / 2, c.Top + c.Height / 2),
            new Point(a.Left + a.Width / 2, a.Top + a.Height / 4),
            Hold);

        _app.WaitForRows(Ids.ListsList, "Inbox", "C", "A", "B");
        Thread.Sleep(500);
        Assert.Null(MenuItem("Rename"));
    }

    [Fact]
    public void A_touch_hold_on_a_List_lifted_without_a_slide_opens_its_context_menu()
    {
        AddList("Groceries");

        var row = AppSession.SettledBounds(() => ListRow("Groceries"));
        var at = new Point(row.Left + row.Width / 2, row.Top + row.Height / 2);
        TouchInput.HoldAndDrag(at, at, Hold);

        AppSession.Eventually(() => MenuItem("Rename"), "A touch hold lifted in place opened no context menu.");
        AppSession.Press(VirtualKeyShort.ESCAPE);
        AppSession.WaitUntil(() => MenuItem("Rename") is null, () => "Esc did not close the context menu.");
        Assert.Equal(["Inbox", "Groceries"], _app.Rows(Ids.ListsList));
    }

    private AutomationElement? MenuItem(string name) =>
        _app.Window.Automation.GetDesktop()
            .FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(name)));

    private void AddTasks(params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++)
        {
            _app.TypeInto(Ids.QuickAddBox, titles[i], pressEnter: true);
            _app.WaitForRows(Ids.OpenTaskList, titles[..(i + 1)]);
        }
    }

    private void AddList(string name)
    {
        _app.Find(Ids.NewListButton).AsButton().Invoke();
        _app.TypeInto(Ids.NewListBox, name, pressEnter: true);
        AppSession.WaitUntil(() => _app.Rows(Ids.ListsList).Contains(name),
            () => $"Lists were [{string.Join(", ", _app.Rows(Ids.ListsList))}].");
    }

    private AutomationElement TaskRow(string title) =>
        AppSession.Eventually(() => _app.FindRow(Ids.OpenTaskList, title), $"No row '{title}'.");

    private AutomationElement ListRow(string name) =>
        AppSession.Eventually(
            () => _app.Find(Ids.ListsList).FindFirstChild(cf => cf.ByControlType(ControlType.ListItem).And(cf.ByName(name))),
            $"No List row '{name}'.");

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
