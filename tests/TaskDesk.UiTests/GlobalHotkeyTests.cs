using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>
/// The global shortcut against the session's real hotkey table, which every app on the desktop shares. The gate's
/// in-process HotkeyRegistrarTests and SettingsWindowTests run against a fake table; these keep the real
/// RegisterHotKey round trip (QuickAddTests presses a real one). The test process plays "another application" by
/// registering combinations itself. Ctrl+Alt+Shift+F9 and F10: nobody uses them, and QuickAddTests uses F11.
/// </summary>
public sealed class GlobalHotkeyTests : IDisposable
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint F9 = 0x78;
    private const uint F10 = 0x79;
    private const string ConflictMessage = "This shortcut is used by another application.";

    // A field, not a using: disposed after the test, so a failure leaves a screenshot (AppSession.CaptureIfFailed).
    private AppSession? _app;

    public void Dispose() => _app?.Dispose();

    [Fact]
    public void A_shortcut_another_application_holds_shows_the_conflict_in_Settings()
    {
        Assert.True(TryHold(F10, 1), "Ctrl+Alt+Shift+F10 is taken before the test: close the app that holds it.");
        try
        {
            var app = _app = AppSession.Start(globalHotkey: "Ctrl+Alt+Shift+F10");
            AppSession.Ctrl(VirtualKeyShort.OEM_COMMA);
            var settings = app.SettingsWindow();

            AppSession.Eventually(() => settings.FindFirstDescendant(cf => cf.ByName(ConflictMessage)),
                "Settings does not say the shortcut is used by another application.");
        }
        finally
        {
            Release(1);
        }
    }

    [Fact]
    public void A_shortcut_changed_in_Settings_is_held_and_the_old_one_is_released()
    {
        var app = _app = AppSession.Start(globalHotkey: "Ctrl+Alt+Shift+F10");
        AppSession.WaitUntil(() => !TryHoldAndRelease(F10), () => "TaskDesk does not hold Ctrl+Alt+Shift+F10.");
        AppSession.Ctrl(VirtualKeyShort.OEM_COMMA);
        var settings = app.SettingsWindow();
        var box = AppSession.Eventually(
            () => settings.FindFirstDescendant(cf => cf.ByName("Global hotkey (press a key combination)")),
            "Settings has no Global hotkey box.").AsTextBox();

        box.Focus();
        AppSession.WaitUntil(() => box.Properties.HasKeyboardFocus.ValueOrDefault,
            () => "The Global hotkey box did not take focus.");
        AppSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.SHIFT, VirtualKeyShort.F9);

        AppSession.WaitUntil(() => box.Text == "Ctrl+Alt+Shift+F9", () => $"The Global hotkey box shows '{box.Text}'.");
        AppSession.WaitUntil(() => !TryHoldAndRelease(F9), () => "TaskDesk does not hold the new Ctrl+Alt+Shift+F9.");
        Assert.True(TryHoldAndRelease(F10), "TaskDesk still holds the old Ctrl+Alt+Shift+F10.");
    }

    // Registrations on no window belong to this thread; the test registers and releases on the same thread.
    private static bool TryHold(uint key, int id) =>
        RegisterHotKey(0, id, ModControl | ModAlt | ModShift | ModNoRepeat, key);

    private static void Release(int id) => UnregisterHotKey(0, id);

    private static bool TryHoldAndRelease(uint key)
    {
        if (!TryHold(key, 2))
        {
            return false;
        }

        Release(2);
        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
