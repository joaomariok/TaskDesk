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

        _app.EndSessionAndRestart();

        _app.WaitForRows(Ids.OpenTaskList, "Draft v2");
    }
}
