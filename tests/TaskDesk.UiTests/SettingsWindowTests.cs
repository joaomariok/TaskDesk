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

    // The real clipboard round trip, kept out of the gate (it is the person's clipboard, shared with every app). The
    // in-process ClipboardServiceTests check the WinUI service's logic against a fake clipboard.
    [Fact]
    public void Copy_path_puts_the_data_folder_on_the_clipboard()
    {
        // Put back the person's text afterwards, when there is some.
        var previous = SystemClipboard.ReadText();
        try
        {
            AppSession.Ctrl(VirtualKeyShort.OEM_COMMA);
            var settings = _app.SettingsWindow();
            var copy = AppSession.Eventually(
                () => settings.FindFirstDescendant(cf => cf.ByName("Copy data folder path")),
                "Settings has no Copy data folder path button.").AsButton();

            copy.Invoke();

            var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_app.DataDirectory));
            AppSession.WaitUntil(
                () => SystemClipboard.ReadText() is { } text
                    && string.Equals(Path.TrimEndingDirectorySeparator(text), expected, StringComparison.OrdinalIgnoreCase),
                () => $"The clipboard holds '{SystemClipboard.ReadText()}', not the data folder '{expected}'.");
        }
        finally
        {
            if (previous is not null)
            {
                SystemClipboard.WriteText(previous);
            }
        }
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
        // Selecting through UI Automation can leave the WPF drop-down open, and it takes the window's Close.
        closeBehavior.Collapse();
        settings.Close();
        AppSession.WaitUntil(() => !_app.SettingsWindowShown(), () => "The Settings window did not close.");

        var handle = _app.Window.Properties.NativeWindowHandle.Value;
        _app.Window.Close();

        AppSession.WaitUntil(() => !AppSession.IsWindowShown(handle), () => "The window is still shown after Close.");
        Assert.True(_app.IsRunning, "TaskDesk exited although Settings chose Hide to tray.");
    }
}
