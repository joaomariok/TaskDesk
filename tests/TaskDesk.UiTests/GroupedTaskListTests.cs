using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>
/// The one grouped Task list with real keyboard input: the arrow keys cross from the Overdue rows into the open
/// ones, walk into an open Task's Subtask rows, and the "Completed (N)" heading is reached and toggled from
/// the keyboard.
/// </summary>
public sealed class GroupedTaskListTests : IDisposable
{
    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Down_at_the_last_Overdue_row_on_Today_selects_the_first_open_row_and_Up_comes_back()
    {
        AppSession.Ctrl(VirtualKeyShort.KEY_4);
        _app.TypeInto(Ids.QuickAddBox, "Pay the rent", pressEnter: true);
        MakeDueYesterday("Pay the rent");
        AppSession.Press(VirtualKeyShort.ESCAPE);

        AppSession.Ctrl(VirtualKeyShort.KEY_1);
        AppSession.WaitUntil(() => _app.Find(Ids.PageHeading).Name == "Today",
            () => $"The page heading was '{_app.Find(Ids.PageHeading).Name}'.");
        _app.TypeInto(Ids.QuickAddBox, "Water plants", pressEnter: true);
        // The headings are not rows: the list's rows are the two Tasks, Overdue first.
        _app.WaitForRows(Ids.OpenTaskList, "Pay the rent", "Water plants");
        _app.SelectRow(Ids.OpenTaskList, "Pay the rent");

        AppSession.Press(VirtualKeyShort.DOWN);
        AppSession.WaitUntil(() => _app.FocusedName() == "Water plants" && IsSelected("Water plants"),
            () => $"Down left focus on '{_app.FocusedName()}'.");

        AppSession.Press(VirtualKeyShort.UP);
        AppSession.WaitUntil(() => _app.FocusedName() == "Pay the rent" && IsSelected("Pay the rent"),
            () => $"Up left focus on '{_app.FocusedName()}'.");
    }

    [Fact]
    public void The_Completed_heading_is_reached_and_toggled_with_the_keyboard()
    {
        _app.TypeInto(Ids.QuickAddBox, "Pay rent", pressEnter: true);
        _app.TypeInto(Ids.QuickAddBox, "Book flights", pressEnter: true);
        _app.SelectRow(Ids.OpenTaskList, "Book flights");
        AppSession.Press(VirtualKeyShort.SPACE);
        _app.WaitForRows(Ids.OpenTaskList, "Pay rent");

        var heading = _app.Find(Ids.CompletedHeading);
        Assert.Equal(ControlType.Button, heading.ControlType);
        AppSession.WaitUntil(() => heading.Name == "Completed (1)", () => $"The heading was named '{heading.Name}'.");
        Assert.Equal(ExpandCollapseState.Collapsed, State(heading));

        // Down from the last open row reaches the heading; Space expands it and Enter collapses it.
        _app.SelectRow(Ids.OpenTaskList, "Pay rent");
        AppSession.Press(VirtualKeyShort.DOWN);
        AppSession.WaitUntil(() => _app.FocusedAutomationId() == Ids.CompletedHeading,
            () => $"Down left focus on '{_app.FocusedName()}'.");
        AppSession.Press(VirtualKeyShort.SPACE);
        _app.WaitForRows(Ids.OpenTaskList, "Pay rent", "Book flights");
        Assert.Equal(ExpandCollapseState.Expanded, State(heading));
        AppSession.Press(VirtualKeyShort.RETURN);
        _app.WaitForRows(Ids.OpenTaskList, "Pay rent");
        Assert.Equal(ExpandCollapseState.Collapsed, State(heading));

        // Delete on the heading deletes nothing.
        AppSession.Press(VirtualKeyShort.DELETE);
        Thread.Sleep(300);
        _app.WaitForRows(Ids.OpenTaskList, "Pay rent");
        Assert.Equal("Completed (1)", heading.Name);

        // Expanded, Down from the heading selects the first completed row.
        AppSession.Press(VirtualKeyShort.SPACE);
        _app.WaitForRows(Ids.OpenTaskList, "Pay rent", "Book flights");
        AppSession.Press(VirtualKeyShort.DOWN);
        AppSession.WaitUntil(() => _app.FocusedName() == "Book flights" && IsSelected("Book flights"),
            () => $"Down from the heading left focus on '{_app.FocusedName()}'.");
    }

    [Fact]
    public void Down_walks_into_a_Subtask_row_Left_selects_its_Task_and_Right_and_Left_open_and_close_it()
    {
        _app.TypeInto(Ids.QuickAddBox, "Plan trip", pressEnter: true);
        _app.TypeInto(Ids.QuickAddBox, "Buy milk", pressEnter: true);
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip", "Buy milk");

        // Two Subtasks, added in the detail pane; Esc closes it and gives the row back its focus.
        _app.SelectRow(Ids.OpenTaskList, "Plan trip");
        AppSession.Press(VirtualKeyShort.RETURN);
        _app.TypeInto(Ids.NewSubtaskBox, "Book hotel", pressEnter: true);
        _app.TypeInto(Ids.NewSubtaskBox, "Pack", pressEnter: true);
        AppSession.WaitUntil(() => _app.TextBox(Ids.NewSubtaskBox).Text.Length == 0, () => "The second Subtask was not added.");
        AppSession.Press(VirtualKeyShort.ESCAPE);
        _app.SelectRow(Ids.OpenTaskList, "Plan trip");

        AppSession.Press(VirtualKeyShort.RIGHT);
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip", "Subtask Book hotel", "Subtask Pack", "Buy milk");

        // Down walks into the Subtask rows and on to the next Task; Up comes back.
        AppSession.Press(VirtualKeyShort.DOWN);
        AppSession.WaitUntil(() => _app.FocusedName() == "Subtask Book hotel" && IsSelected("Subtask Book hotel"),
            () => $"Down left focus on '{_app.FocusedName()}'.");
        AppSession.Press(VirtualKeyShort.DOWN);
        AppSession.Press(VirtualKeyShort.DOWN);
        AppSession.WaitUntil(() => _app.FocusedName() == "Buy milk" && IsSelected("Buy milk"),
            () => $"Down left focus on '{_app.FocusedName()}'.");
        AppSession.Press(VirtualKeyShort.UP);
        AppSession.WaitUntil(() => _app.FocusedName() == "Subtask Pack" && IsSelected("Subtask Pack"),
            () => $"Up left focus on '{_app.FocusedName()}'.");

        // Delete on a Subtask row deletes nothing.
        AppSession.Press(VirtualKeyShort.DELETE);
        Thread.Sleep(300);
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip", "Subtask Book hotel", "Subtask Pack", "Buy milk");

        // Left on a Subtask row selects its Task, which stays open; Left again closes it.
        AppSession.Press(VirtualKeyShort.LEFT);
        AppSession.WaitUntil(() => _app.FocusedName() == "Plan trip" && IsSelected("Plan trip"),
            () => $"Left left focus on '{_app.FocusedName()}'.");
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip", "Subtask Book hotel", "Subtask Pack", "Buy milk");
        AppSession.Press(VirtualKeyShort.LEFT);
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip", "Buy milk");
        AppSession.Press(VirtualKeyShort.RIGHT);
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip", "Subtask Book hotel", "Subtask Pack", "Buy milk");
    }

    private static ExpandCollapseState State(AutomationElement heading) =>
        heading.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value;

    private bool IsSelected(string title) =>
        _app.FindRow(Ids.OpenTaskList, title)?.AsListBoxItem().IsSelected == true;

    // Opens the Task's details and picks yesterday in the Due date calendar, then waits for the save.
    private void MakeDueYesterday(string title)
    {
        _app.SelectRow(Ids.OpenTaskList, title);
        Keyboard.Type(VirtualKeyShort.F2);
        AppSession.WaitUntil(() => _app.TextBox(Ids.DetailTitleBox).Text == title, () => "The detail pane did not open.");

        var yesterday = DateTime.Today.AddDays(-1);
        _app.Find(Ids.DetailDueDatePicker).Click();
        // The calendar flyout is a pop-up, so look from the desktop.
        var calendar = AppSession.Eventually(
            () => _app.Window.Automation.GetDesktop().FindFirstDescendant(cf => cf.ByClassName("CalendarView")),
            "The Due date picker opened no calendar.");
        if (yesterday.Month != DateTime.Today.Month)
        {
            AppSession.Eventually(() => calendar.FindFirstDescendant(cf => cf.ByAutomationId("PreviousButton")),
                "The Due date calendar has no previous-month button.").AsButton().Invoke();
        }

        AutomationElement? Yesterday()
        {
            // The month's days start at its first "1"; the shown days before it belong to the month before.
            var shown = calendar.FindAllDescendants(cf => cf.ByClassName("CalendarViewDayItem"))
                .Where(item => !item.IsOffscreen).ToList();
            var first = shown.FindIndex(item => IsDay(item, 1));
            var index = first + yesterday.Day - 1;
            return first >= 0 && index < shown.Count && IsDay(shown[index], yesterday.Day) ? shown[index] : null;
        }

        AppSession.Eventually(Yesterday, $"The Due date calendar does not show {yesterday:D}.").Click();

        // The Due edit saves after a short debounce; Overdue shows the Task once it has.
        AppSession.WaitUntil(() =>
            {
                AppSession.Ctrl(VirtualKeyShort.KEY_3);
                Thread.Sleep(300);
                return _app.Rows(Ids.OpenTaskList).Contains(title);
            },
            () => $"Overdue rows were [{string.Join(", ", _app.Rows(Ids.OpenTaskList))}]; the Due did not save.");
    }

    // A day is named by its number; today's adds ", today".
    private static bool IsDay(AutomationElement item, int day) =>
        item.Name.Split(',')[0] == day.ToString(CultureInfo.CurrentCulture);
}
