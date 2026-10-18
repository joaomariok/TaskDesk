using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>
/// Windows signing out or shutting down, in a real process (MANUAL-TESTS: Tray and window): the app lets the
/// session end, but only after the pending detail edit is saved. Framework-neutral: it sends the session-end
/// messages to every top-level window of the app, as Windows does.
/// </summary>
public sealed class SessionEndTests : IDisposable
{
    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void A_pending_title_edit_survives_Windows_signing_out()
    {
        _app.TypeInto(Ids.QuickAddBox, "Draft", pressEnter: true);
        _app.SelectRow(Ids.OpenTaskList, "Draft");
        Keyboard.Type(VirtualKeyShort.F2);
        AppSession.WaitUntil(() => _app.TextBox(Ids.DetailTitleBox).Text == "Draft", () => "Details of 'Draft' did not open.");
        _app.Focus(Ids.DetailTitleBox);
        Keyboard.Type(VirtualKeyShort.END);
        Keyboard.Type(" v2");
        // Typed keys wait in the app's input queue, but Windows' session-end messages are sent, and a thread handles
        // sent messages before its queued input: wait until the edit is made, as a person's edit is before they sign out.
        AppSession.WaitUntil(() => _app.TextBox(Ids.DetailTitleBox).Text == "Draft v2",
            () => $"The title box holds '{_app.TextBox(Ids.DetailTitleBox).Text}', not the edit.");

        _app.EndSessionAndRestart();

        _app.WaitForRows(Ids.OpenTaskList, "Draft v2");
    }
}
