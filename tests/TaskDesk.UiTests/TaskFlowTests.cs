using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>Adding, completing, deleting and undoing Tasks from the keyboard (MANUAL-TESTS: Keyboard-only).</summary>
public sealed class TaskFlowTests : IDisposable
{
    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Start_up_puts_keyboard_focus_in_quick_add()
    {
        AppSession.WaitUntil(() => _app.FocusedAutomationId() == Ids.QuickAddBox,
            () => $"Focus was on '{_app.FocusedAutomationId()}'.");
    }

    [Fact]
    public void Enter_in_quick_add_adds_the_Task_and_clears_the_box()
    {
        _app.TypeInto(Ids.QuickAddBox, "Buy milk", pressEnter: true);

        _app.WaitForRows(Ids.OpenTaskList, "Buy milk");
        AppSession.WaitUntil(() => _app.TextBox(Ids.QuickAddBox).Text.Length == 0, () => "Quick add was not cleared.");
    }

    [Fact]
    public void Space_completes_the_selected_Task()
    {
        _app.TypeInto(Ids.QuickAddBox, "Water plants", pressEnter: true);
        _app.SelectRow(Ids.OpenTaskList, "Water plants");

        AppSession.Press(VirtualKeyShort.SPACE);

        _app.WaitForRows(Ids.OpenTaskList);
        AppSession.Ctrl(VirtualKeyShort.KEY_5);
        _app.WaitForRows(Ids.CompletedTaskList, "Water plants");
    }

    [Fact]
    public void The_round_Task_checkbox_on_a_row_is_a_checkbox_and_Space_on_it_completes_the_Task()
    {
        _app.TypeInto(Ids.QuickAddBox, "Water plants", pressEnter: true);
        var row = AppSession.Eventually(
            () => _app.Find(Ids.OpenTaskList).FindFirstChild(cf => cf.ByControlType(ControlType.ListItem).And(cf.ByName("Water plants"))),
            "No row 'Water plants'.");
        var check = AppSession.Eventually(() => row.FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox)),
            "The row has no checkbox.");

        Assert.Equal("Completed", check.Name);
        Assert.True(check.Patterns.Toggle.IsSupported, "The Task checkbox does not expose the toggle pattern.");
        Assert.Equal(ToggleState.Off, check.Patterns.Toggle.Pattern.ToggleState.Value);

        check.Focus();
        AppSession.WaitUntil(() => check.Properties.HasKeyboardFocus.ValueOrDefault, () => "The Task checkbox did not take focus.");
        AppSession.Press(VirtualKeyShort.SPACE);

        _app.WaitForRows(Ids.OpenTaskList);
        AppSession.Ctrl(VirtualKeyShort.KEY_5);
        _app.WaitForRows(Ids.CompletedTaskList, "Water plants");
    }

    [Fact]
    public void The_detail_pane_has_the_round_Task_checkbox_and_Space_on_it_completes_the_Task()
    {
        _app.TypeInto(Ids.QuickAddBox, "Book dentist", pressEnter: true);
        _app.SelectRow(Ids.OpenTaskList, "Book dentist");
        AppSession.Press(VirtualKeyShort.F2);
        AppSession.WaitUntil(() => _app.TextBox(Ids.DetailTitleBox).Text == "Book dentist",
            () => "The detail pane did not open.");

        var check = _app.Find(Ids.DetailCompletedBox);
        Assert.Equal(ControlType.CheckBox, check.ControlType);
        Assert.Equal("Completed", check.Name);
        Assert.True(check.Patterns.Toggle.IsSupported, "The detail pane checkbox does not expose the toggle pattern.");

        _app.Focus(Ids.DetailCompletedBox);
        AppSession.Press(VirtualKeyShort.SPACE);

        _app.WaitForRows(Ids.OpenTaskList);
        AppSession.WaitUntil(() => check.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On,
            () => "The detail pane checkbox did not show the Task as completed.");
        AppSession.Ctrl(VirtualKeyShort.KEY_5);
        _app.WaitForRows(Ids.CompletedTaskList, "Book dentist");
    }

    [Fact]
    public void Delete_then_the_Undo_bar_restores_the_Task()
    {
        _app.TypeInto(Ids.QuickAddBox, "Renew passport", pressEnter: true);
        _app.SelectRow(Ids.OpenTaskList, "Renew passport");

        AppSession.Press(VirtualKeyShort.DELETE);
        _app.WaitForRows(Ids.OpenTaskList);
        _app.Find(Ids.UndoButton).AsButton().Invoke();

        _app.WaitForRows(Ids.OpenTaskList, "Renew passport");
    }

    [Fact]
    public void The_Undo_bar_is_an_InfoBar_named_Task_deleted_with_the_message_and_an_Undo_action()
    {
        _app.TypeInto(Ids.QuickAddBox, "Renew passport", pressEnter: true);
        _app.SelectRow(Ids.OpenTaskList, "Renew passport");

        AppSession.Press(VirtualKeyShort.DELETE);

        var undo = AppSession.Eventually(() => _app.Find(Ids.UndoButton), "No Undo button.");
        Assert.Equal("Undo", undo.Name);
        var bar = AppSession.Eventually(() => _app.Window.FindFirstDescendant(cf => cf.ByName("Task deleted")),
            "The Undo bar has no accessible name 'Task deleted'.");
        Assert.True(bar.IsAvailable);
    }

    [Fact]
    public void Delete_then_Ctrl_Z_restores_the_Task()
    {
        _app.TypeInto(Ids.QuickAddBox, "Renew passport", pressEnter: true);
        _app.SelectRow(Ids.OpenTaskList, "Renew passport");

        AppSession.Press(VirtualKeyShort.DELETE);
        _app.WaitForRows(Ids.OpenTaskList);
        AppSession.Ctrl(VirtualKeyShort.KEY_Z);

        _app.WaitForRows(Ids.OpenTaskList, "Renew passport");
    }
}
