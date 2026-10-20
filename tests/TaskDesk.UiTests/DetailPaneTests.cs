using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>The detail pane opens on demand, closes with Esc, and docks or overlays by window width (1100 px).</summary>
public sealed class DetailPaneTests : IDisposable
{
    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    private bool PaneIsOpen() =>
        _app.Window.FindFirstDescendant(cf => cf.ByAutomationId(Ids.DetailTitleBox)) is not null;

    private void AddAndSelect(string title)
    {
        _app.TypeInto(Ids.QuickAddBox, title, pressEnter: true);
        _app.WaitForRows(Ids.OpenTaskList, title);
        _app.SelectRow(Ids.OpenTaskList, title);
    }

    [Fact]
    public void Enter_on_a_row_opens_the_pane_on_its_Task()
    {
        AddAndSelect("Book dentist");
        Assert.False(PaneIsOpen(), "Selecting a row opened the pane in a narrow window.");

        AppSession.Press(VirtualKeyShort.RETURN);

        AppSession.WaitUntil(() => PaneIsOpen() && _app.TextBox(Ids.DetailTitleBox).Text == "Book dentist",
            () => "Enter did not open the pane on the Task.");
    }

    [Fact]
    public void Esc_closes_the_pane_and_returns_focus_to_the_Tasks_row()
    {
        AddAndSelect("Book dentist");
        AppSession.Press(VirtualKeyShort.RETURN);
        AppSession.WaitUntil(PaneIsOpen, () => "The pane did not open.");

        AppSession.Press(VirtualKeyShort.ESCAPE);

        AppSession.WaitUntil(() => !PaneIsOpen() && _app.FocusedName() == "Book dentist",
            () => $"Esc left the pane open or focus on '{_app.FocusedName()}'.");
    }

    [Fact]
    public void Esc_closes_the_Reminder_flyout_without_saving_and_returns_focus_to_Add_reminder()
    {
        _app.ResizeTo(1200, 800);
        AddAndSelect("Book dentist");
        AppSession.Press(VirtualKeyShort.RETURN);
        AppSession.WaitUntil(PaneIsOpen, () => "The pane did not open.");
        _app.Focus(Ids.AddReminderButton);

        AppSession.Press(VirtualKeyShort.RETURN);
        AppSession.WaitUntil(
            () => _app.Window.FindFirstDescendant(cf => cf.ByAutomationId(Ids.ReminderDatePicker)) is not null,
            () => "Enter on Add reminder did not open the flyout.");
        AppSession.Press(VirtualKeyShort.ESCAPE);

        AppSession.WaitUntil(
            () => _app.Window.FindFirstDescendant(cf => cf.ByAutomationId(Ids.ReminderDatePicker)) is null
                && _app.FocusedAutomationId() == Ids.AddReminderButton,
            () => $"Esc left the flyout open or focus on '{_app.FocusedAutomationId()}'.");
        Assert.True(PaneIsOpen(), "Esc in the flyout closed the pane too.");
    }

    [Fact]
    public void The_pane_overlays_the_list_at_1100_px_and_docks_beside_it_above()
    {
        AddAndSelect("Book dentist");
        AppSession.Press(VirtualKeyShort.RETURN);
        AppSession.WaitUntil(PaneIsOpen, () => "The pane did not open.");

        _app.ResizeTo(1100, 600);
        AppSession.WaitUntil(
            () => _app.Find(Ids.DetailPane).BoundingRectangle.Left < _app.Find(Ids.OpenTaskList).BoundingRectangle.Right,
            () => "At 1100 px the pane did not overlay the list.");

        _app.ResizeTo(1200, 600);
        AppSession.WaitUntil(
            () => _app.Find(Ids.DetailPane).BoundingRectangle.Left >= _app.Find(Ids.OpenTaskList).BoundingRectangle.Right,
            () => "Above 1100 px the pane did not dock beside the list.");
    }

    [Fact]
    public void A_docked_pane_closed_with_Esc_stays_closed_while_arrow_keys_move_the_selection()
    {
        _app.ResizeTo(1200, 600);
        _app.TypeInto(Ids.QuickAddBox, "First", pressEnter: true);
        _app.TypeInto(Ids.QuickAddBox, "Second", pressEnter: true);
        _app.WaitForRows(Ids.OpenTaskList, "First", "Second");
        _app.SelectRow(Ids.OpenTaskList, "First");
        AppSession.Press(VirtualKeyShort.RETURN);
        AppSession.WaitUntil(PaneIsOpen, () => "The pane did not open.");
        AppSession.Press(VirtualKeyShort.ESCAPE);
        AppSession.WaitUntil(() => !PaneIsOpen() && _app.FocusedName() == "First",
            () => $"Esc left the pane open or focus on '{_app.FocusedName()}'.");

        AppSession.Press(VirtualKeyShort.DOWN);
        AppSession.WaitUntil(() => _app.FocusedName() == "Second", () => $"Focus was on '{_app.FocusedName()}'.");

        Assert.False(PaneIsOpen(), "An arrow key reopened the closed pane.");
    }

    [Fact]
    public void An_overlaid_pane_stays_on_its_Task_while_arrow_keys_move_the_selection()
    {
        _app.TypeInto(Ids.QuickAddBox, "First", pressEnter: true);
        _app.TypeInto(Ids.QuickAddBox, "Second", pressEnter: true);
        _app.WaitForRows(Ids.OpenTaskList, "First", "Second");
        _app.SelectRow(Ids.OpenTaskList, "First");
        AppSession.Press(VirtualKeyShort.RETURN);
        AppSession.WaitUntil(() => PaneIsOpen() && _app.TextBox(Ids.DetailTitleBox).Text == "First", () => "The pane did not open.");
        _app.SelectRow(Ids.OpenTaskList, "Second");

        Assert.Equal("First", _app.TextBox(Ids.DetailTitleBox).Text);
    }
}
