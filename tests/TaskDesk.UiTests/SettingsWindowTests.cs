using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>
/// The Settings window in a real process (MANUAL-TESTS: Tray and window): Ctrl+comma opens it, and its close-behaviour
/// choice decides what closing the main window does. Framework-neutral, like <see cref="CloseToTrayTests"/>.
/// </summary>
public sealed class SettingsWindowTests : IDisposable
{
    // Close exits at start, so the Hide-to-tray choice made in Settings is what the test sees.
    private readonly AppSession _app = AppSession.Start(closeBehavior: "Exit");

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Ctrl_comma_opens_Settings_and_closing_it_closes_the_window()
    {
        AppSession.Ctrl(VirtualKeyShort.OEM_COMMA);
        var settings = _app.SettingsWindow();
        AppSession.Eventually(() => settings.FindFirstDescendant(cf => cf.ByName("Close button behavior")),
            "Settings has no Close button behavior control.");

        settings.Close();

        AppSession.WaitUntil(() => !_app.SettingsWindowShown(), () => "The Settings window did not close.");
    }

    [Fact]
    public void Choosing_Hide_to_tray_in_Settings_makes_closing_the_main_window_hide_it()
    {
        AppSession.Ctrl(VirtualKeyShort.OEM_COMMA);
        var settings = _app.SettingsWindow();
        var closeBehavior = AppSession.Eventually(
            () => settings.FindFirstDescendant(cf => cf.ByName("Close button behavior")),
            "Settings has no Close button behavior control.").AsComboBox();

        closeBehavior.Select("HideToTray");
        AppSession.WaitUntil(() => closeBehavior.SelectedItem?.Name == "HideToTray",
            () => $"The close behaviour shows '{closeBehavior.SelectedItem?.Name}'.");
        settings.Close();
        AppSession.WaitUntil(() => !_app.SettingsWindowShown(), () => "The Settings window did not close.");

        var handle = _app.Window.Properties.NativeWindowHandle.Value;
        _app.Window.Close();

        AppSession.WaitUntil(() => !AppSession.IsWindowShown(handle), () => "The window is still shown after Close.");
        Assert.True(_app.IsRunning, "TaskDesk exited although Settings chose Hide to tray.");
    }
}
