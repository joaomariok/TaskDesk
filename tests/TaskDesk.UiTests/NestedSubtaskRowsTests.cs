using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>
/// A Task's Subtask rows in the real window: real Right and Left keys on a focused Task row open and close it, and a
/// screen reader reads and drives the row through UI Automation's ExpandCollapse pattern.
/// </summary>
public sealed class NestedSubtaskRowsTests : IDisposable
{
    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Right_and_Left_on_a_Task_row_open_and_close_its_Subtask_rows_and_UI_Automation_reads_the_state()
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
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip", "Buy milk");
        _app.SelectRow(Ids.OpenTaskList, "Plan trip");
        var row = _app.FindRow(Ids.OpenTaskList, "Plan trip")!;
        Assert.Equal(ExpandCollapseState.Collapsed, row.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value);

        AppSession.Press(VirtualKeyShort.RIGHT);
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip", "Subtask Book hotel", "Subtask Pack", "Buy milk");
        AppSession.WaitUntil(
            () => row.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value == ExpandCollapseState.Expanded,
            () => "The open Task row does not read as expanded.");
        var box = AppSession.Eventually(
            () => _app.FindRow(Ids.OpenTaskList, "Subtask Pack")?.FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox)),
            "The Subtask row has no checkbox.");
        Assert.Equal("Subtask done", box.Name);
        Assert.Equal(ToggleState.Off, box.Patterns.Toggle.Pattern.ToggleState.Value);

        AppSession.Press(VirtualKeyShort.LEFT);
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip", "Buy milk");

        // A screen reader's Expand and Collapse do the same.
        _app.FindRow(Ids.OpenTaskList, "Plan trip")!.Patterns.ExpandCollapse.Pattern.Expand();
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip", "Subtask Book hotel", "Subtask Pack", "Buy milk");
        _app.FindRow(Ids.OpenTaskList, "Plan trip")!.Patterns.ExpandCollapse.Pattern.Collapse();
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip", "Buy milk");
    }
}
