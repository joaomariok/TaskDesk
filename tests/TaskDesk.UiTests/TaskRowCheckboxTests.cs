using FlaUI.Core.Definitions;

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
}
