using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>
/// The Task checkbox on a row, driven the way a screen reader drives it (UI Automation's Toggle pattern),
/// completes the Task end to end: the row leaves the open Tasks and the completion is saved.
/// </summary>
public sealed class TaskRowCheckboxTests : IDisposable
{
    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void The_Toggle_pattern_on_the_row_checkbox_completes_the_Task_and_the_completion_is_saved()
    {
        _app.TypeInto(Ids.QuickAddBox, "Water plants", pressEnter: true);
        _app.WaitForRows(Ids.OpenTaskList, "Water plants");
        var row = AppSession.Eventually(
            () => _app.FindRow(Ids.OpenTaskList, "Water plants"),
            "No row 'Water plants'.");
        var check = AppSession.Eventually(() => row.FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox)),
            "The row has no checkbox.");
        Assert.Equal("Completed", check.Name);
        Assert.Equal(ToggleState.Off, check.Patterns.Toggle.Pattern.ToggleState.Value);

        check.Patterns.Toggle.Pattern.Toggle();

        _app.WaitForRows(Ids.OpenTaskList);
        _app.Restart();
        _app.WaitForRows(Ids.OpenTaskList);
    }

    [Fact]
    public void The_Toggle_pattern_on_a_Subtask_row_checkbox_ticks_it_and_the_tick_survives_a_restart()
    {
        _app.TypeInto(Ids.QuickAddBox, "Plan trip", pressEnter: true);
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip");
        _app.SelectRow(Ids.OpenTaskList, "Plan trip");
        AppSession.Press(VirtualKeyShort.RETURN);
        _app.TypeInto(Ids.NewSubtaskBox, "Pack", pressEnter: true);
        AppSession.WaitUntil(() => _app.TextBox(Ids.NewSubtaskBox).Text.Length == 0, () => "The Subtask was not added.");
        AppSession.Press(VirtualKeyShort.ESCAPE);
        _app.SelectRow(Ids.OpenTaskList, "Plan trip");
        AppSession.Press(VirtualKeyShort.RIGHT);
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip", "Subtask Pack");

        var box = SubtaskBox();
        Assert.Equal(ToggleState.Off, box.Patterns.Toggle.Pattern.ToggleState.Value);
        box.Patterns.Toggle.Pattern.Toggle();
        AppSession.WaitUntil(
            () => SubtaskBox().Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On,
            () => "The Subtask box did not show the tick.");

        _app.Restart();
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip");
        _app.SelectRow(Ids.OpenTaskList, "Plan trip");
        AppSession.Press(VirtualKeyShort.RIGHT);
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip", "Subtask Pack");
        Assert.Equal(ToggleState.On, SubtaskBox().Patterns.Toggle.Pattern.ToggleState.Value);
    }

    private FlaUI.Core.AutomationElements.AutomationElement SubtaskBox() => AppSession.Eventually(
        () => _app.FindRow(Ids.OpenTaskList, "Subtask Pack")?.FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox)),
        "The Subtask row has no checkbox.");
}
