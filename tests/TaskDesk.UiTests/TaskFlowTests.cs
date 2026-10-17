using FlaUI.Core.AutomationElements;
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
