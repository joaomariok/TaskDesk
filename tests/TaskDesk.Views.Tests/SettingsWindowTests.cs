using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.App.Shell;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Shell;
using TaskDesk.Presentation;
using Windows.System;

namespace TaskDesk.Views.Tests;

/// <summary>
/// "TaskDesk Settings" as native setting cards: every setting saves as it changes, the theme applies live, the
/// global shortcut is captured by pressing it, and the data folder opens and copies.
/// </summary>
[TestClass]
public sealed class SettingsWindowTests
{
    private static readonly Hotkey Free = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x77); // F8
    private static readonly Hotkey Taken = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x76); // F7

    private const VirtualKeyModifiers CtrlAltShift =
        VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift;

    // The registrars register in a fake of the session's hotkey table, never the real one (shared with every app).
    private readonly FakeHotkeyTable _hotkeyTable = new();

    // Start with Windows off, so switching it on is a change.
    private readonly InMemorySettingsStore _store = new() { Current = new ShellSettings(StartWithWindows: false) };
    private readonly InMemoryRunKey _runKey = new();
    private readonly RecordingFolderOpener _opener = new();
    private readonly RecordingClipboard _clipboard = new();
    private readonly FakeExport _export = new(@"C:\exports\tasks.json");
    private readonly IAppPaths _paths = AppPaths.ForRoot(Path.Combine(Path.GetTempPath(), "TaskDesk-views-tests"));

    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task The_window_is_TaskDesk_Settings_with_its_settings_on_native_cards()
    {
        var ct = TestContext.CancellationToken;
        using var hotkeys = NewHotkeys();
        var window = await OpenAsync(NewViewModel(new ThemeService(), hotkeys), ct);
        try
        {
            Assert.AreEqual("TaskDesk Settings", window.Title);
            var headers = VisualTree.Descendants(window.View).OfType<SettingsCard>().Select(c => c.Header as string).ToList();
            foreach (var header in new[]
                     {
                         "Theme", "Start with Windows", "Close button", "Global shortcut", "Default reminder time",
                         "Data folder", "Export", "Import", "Back up", "Log folder",
                     })
            {
                CollectionAssert.Contains(headers, header);
            }

            // The accessible names the UI tests and screen readers use, kept from the WPF window.
            Assert.IsNotNull(Named<ComboBox>(window, "Theme"));
            var closeBox = Named<ComboBox>(window, "Close button behavior");
            // The choice reads as its name, as in the WPF window.
            CollectionAssert.Contains(VisualTree.Descendants(closeBox).OfType<TextBlock>().Select(t => t.Text).ToList(), "HideToTray");
            Assert.IsNotNull(Named<ToggleSwitch>(window, "Start with Windows"));
            Assert.IsNotNull(Named<TextBox>(window, "Global hotkey (press a key combination)"));
            Assert.IsNotNull(Named<TimePicker>(window, "Default reminder time"));
            foreach (var button in new[]
                     {
                         "Open data folder", "Copy data folder path", "Copy log folder path", "Export everything to JSON",
                         "Export tasks to CSV", "Import a JSON export", "Back up now",
                     })
            {
                Assert.IsNotNull(Named<Button>(window, button));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Each_setting_saves_as_it_changes()
    {
        var ct = TestContext.CancellationToken;
        using var hotkeys = NewHotkeys();
        var window = await OpenAsync(NewViewModel(new ThemeService(), hotkeys), ct);
        try
        {
            Named<ComboBox>(window, "Close button behavior").SelectedItem = CloseBehavior.Exit;
            Assert.AreEqual(CloseBehavior.Exit, _store.Current.CloseBehavior);

            Named<ToggleSwitch>(window, "Start with Windows").IsOn = true;
            Assert.IsTrue(_store.Current.StartWithWindows);
            Assert.AreEqual("\"C:\\TaskDesk\\TaskDesk.exe\" --minimized", _runKey.GetValue(AutoStartService.ValueName));

            Named<TimePicker>(window, "Default reminder time").SelectedTime = new TimeSpan(8, 30, 0);
            await UiWait.UntilAsync(() => _store.Current.DefaultReminderTime == new TimeOnly(8, 30),
                () => $"The reminder time saved is {_store.Current.DefaultReminderTime}.", ct);

            Named<ComboBox>(window, "Theme").SelectedItem = ThemeChoice.Light;
            Assert.AreEqual(ThemeChoice.Light, _store.Current.Theme);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_default_reminder_time_picker_shows_the_saved_time_in_one_minute_steps()
    {
        var ct = TestContext.CancellationToken;
        _store.Current = _store.Current with { DefaultReminderTime = new TimeOnly(7, 5) };
        using var hotkeys = NewHotkeys();
        var window = await OpenAsync(NewViewModel(new ThemeService(), hotkeys), ct);
        try
        {
            var picker = Named<TimePicker>(window, "Default reminder time");
            Assert.AreEqual(new TimeSpan(7, 5, 0), picker.SelectedTime);
            Assert.AreEqual(1, picker.MinuteIncrement);
            Assert.AreEqual("DefaultReminderTimePicker", AutomationProperties.GetAutomationId(picker));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Choosing_a_theme_applies_it_live_to_the_open_windows()
    {
        var ct = TestContext.CancellationToken;
        var themes = new ThemeService();
        using var hotkeys = NewHotkeys();
        var window = await OpenAsync(NewViewModel(themes, hotkeys), ct);
        try
        {
            themes.Register(window);

            Named<ComboBox>(window, "Theme").SelectedItem = ThemeChoice.Dark;

            await UiWait.UntilAsync(() => window.View.ActualTheme == ElementTheme.Dark,
                () => $"Settings shows {window.View.ActualTheme}.", ct);
            Assert.AreEqual(TitleBarTheme.Dark, window.AppWindow.TitleBar.PreferredTheme);

            Named<ComboBox>(window, "Theme").SelectedItem = ThemeChoice.Light;

            await UiWait.UntilAsync(() => window.View.ActualTheme == ElementTheme.Light,
                () => $"Settings shows {window.View.ActualTheme}.", ct);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Pressing_a_free_combination_makes_it_the_shortcut()
    {
        var ct = TestContext.CancellationToken;
        using var hotkeys = NewHotkeys();
        var window = await OpenAsync(NewViewModel(new ThemeService(), hotkeys), ct);
        try
        {
            var box = Named<TextBox>(window, "Global hotkey (press a key combination)");

            Assert.IsFalse(window.View.CaptureHotkey(VirtualKey.Tab, VirtualKeyModifiers.None), "Tab alone moves the focus.");
            Assert.IsTrue(window.View.CaptureHotkey(VirtualKey.Control, VirtualKeyModifiers.Control), "Ctrl alone waits.");
            Assert.AreEqual("Ctrl+Alt+Space", box.Text);
            Assert.IsTrue(window.View.CaptureHotkey(VirtualKey.F8, CtrlAltShift));

            await UiWait.UntilAsync(() => box.Text == "Ctrl+Alt+Shift+F8", () => $"The box shows {box.Text}.", ct);
            Assert.AreEqual(Free, _store.Current.Hotkey);
            Assert.IsFalse(Named<InfoBar>(window, "Shortcut problem").IsOpen);
            // The combination is registered now: nobody else can take it.
            using var otherWindow = new MessageWindow();
            using var other = new WinUiHotkeyRegistrar(otherWindow, NullLogger<WinUiHotkeyRegistrar>.Instance, _hotkeyTable);
            Assert.IsFalse(other.TryRegister(Free), "The captured shortcut is not registered.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Pressing_a_taken_combination_says_so_and_keeps_the_shortcut()
    {
        var ct = TestContext.CancellationToken;
        using var ownerWindow = new MessageWindow();
        using var owner = new WinUiHotkeyRegistrar(ownerWindow, NullLogger<WinUiHotkeyRegistrar>.Instance, _hotkeyTable);
        Assert.IsTrue(owner.TryRegister(Taken));
        using var hotkeys = NewHotkeys();
        var window = await OpenAsync(NewViewModel(new ThemeService(), hotkeys), ct);
        try
        {
            var box = Named<TextBox>(window, "Global hotkey (press a key combination)");
            var problem = Named<InfoBar>(window, "Shortcut problem");
            Assert.IsFalse(problem.IsOpen);

            Assert.IsTrue(window.View.CaptureHotkey(VirtualKey.F7, CtrlAltShift));

            await UiWait.UntilAsync(() => problem.IsOpen, () => "The shortcut problem did not show.", ct);
            Assert.AreEqual(HotkeyService.ConflictMessage, problem.Message);
            Assert.AreEqual("Ctrl+Alt+Space", box.Text);
            Assert.AreEqual(Hotkey.Default, _store.Current.Hotkey);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_shortcut_problem_closes_once_a_free_combination_is_pressed()
    {
        var ct = TestContext.CancellationToken;
        using var ownerWindow = new MessageWindow();
        using var owner = new WinUiHotkeyRegistrar(ownerWindow, NullLogger<WinUiHotkeyRegistrar>.Instance, _hotkeyTable);
        Assert.IsTrue(owner.TryRegister(Taken));
        using var hotkeys = NewHotkeys();
        var window = await OpenAsync(NewViewModel(new ThemeService(), hotkeys), ct);
        try
        {
            var problem = Named<InfoBar>(window, "Shortcut problem");
            Assert.IsTrue(window.View.CaptureHotkey(VirtualKey.F7, CtrlAltShift));
            await UiWait.UntilAsync(() => problem.IsOpen, () => "The shortcut problem did not show.", ct);

            Assert.IsTrue(window.View.CaptureHotkey(VirtualKey.F8, CtrlAltShift));
            window.View.UpdateLayout();

            Assert.IsNull(window.View.ViewModel.HotkeyError);
            Assert.IsFalse(problem.IsOpen, "The shortcut problem stays open after the error cleared.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_status_line_takes_no_room_while_it_has_no_text()
    {
        var ct = TestContext.CancellationToken;
        using var hotkeys = NewHotkeys();
        var viewModel = NewViewModel(new ThemeService(), hotkeys);
        var window = await OpenAsync(viewModel, ct);
        try
        {
            viewModel.ExportStatus = "Exported";
            viewModel.ImportStatus = "Imported";
            viewModel.BackupStatus = "Backed up";
            window.View.UpdateLayout();
            var statuses = VisualTree.Descendants(window.View).OfType<TextBlock>()
                .Where(t => t.Text is "Exported" or "Imported" or "Backed up").ToList();
            Assert.HasCount(3, statuses);
            Assert.IsTrue(statuses.All(t => t.DesiredSize.Height > 0), "A status that has text is not shown.");

            viewModel.ExportStatus = "";
            viewModel.ImportStatus = "";
            viewModel.BackupStatus = "";
            window.View.UpdateLayout();

            Assert.IsTrue(statuses.All(t => t.Visibility == Visibility.Collapsed && t.DesiredSize.Height == 0),
                "A cleared status still takes room.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_data_folder_opens_and_its_path_copies()
    {
        var ct = TestContext.CancellationToken;
        using var hotkeys = NewHotkeys();
        var window = await OpenAsync(NewViewModel(new ThemeService(), hotkeys), ct);
        try
        {
            Click(Named<Button>(window, "Open data folder"));
            Click(Named<Button>(window, "Copy data folder path"));
            Click(Named<Button>(window, "Copy log folder path"));

            CollectionAssert.AreEqual(new[] { _paths.DataDirectory }, _opener.Opened);
            CollectionAssert.AreEqual(new[] { _paths.DataDirectory, _paths.LogsDirectory }, _clipboard.Copied);
            var texts = VisualTree.Descendants(window.View).OfType<TextBlock>().Select(t => t.Text).ToList();
            CollectionAssert.Contains(texts, _paths.DataDirectory, "The card shows the data folder.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Export_writes_the_picked_file_and_says_where()
    {
        var ct = TestContext.CancellationToken;
        using var hotkeys = NewHotkeys();
        var window = await OpenAsync(NewViewModel(new ThemeService(), hotkeys), ct);
        try
        {
            Click(Named<Button>(window, "Export everything to JSON"));

            await UiWait.UntilAsync(
                () => VisualTree.Descendants(window.View).OfType<TextBlock>()
                    .Any(t => t.Text == @"Exported to C:\exports\tasks.json" && t.Visibility == Visibility.Visible),
                () => "The export status did not show.", ct);
            CollectionAssert.AreEqual(new[] { @"C:\exports\tasks.json" }, _export.Written);
        }
        finally
        {
            window.Close();
        }
    }

    private HotkeyService NewHotkeys()
    {
        var messages = new MessageWindow();
        var registrar = new WinUiHotkeyRegistrar(messages, NullLogger<WinUiHotkeyRegistrar>.Instance, _hotkeyTable);
        var service = new HotkeyService(registrar, _store);
        // The registrar does not own its message window; dispose it once the service is disposed.
        _messageWindows.Add(messages);
        return service;
    }

    private readonly List<MessageWindow> _messageWindows = [];

    [TestCleanup]
    public void DisposeMessageWindows()
    {
        foreach (var window in _messageWindows)
        {
            window.Dispose();
        }
    }

    private SettingsViewModel NewViewModel(ThemeService themes, HotkeyService hotkeys) => new(
        _store,
        new AutoStartService(_runKey),
        themes,
        _opener,
        _paths,
        exePath: @"C:\TaskDesk\TaskDesk.exe",
        version: "1.2.3",
        hotkeys,
        exports: _export,
        exportDialogs: _export,
        clipboard: _clipboard);

    private static async Task<SettingsWindow> OpenAsync(SettingsViewModel viewModel, CancellationToken ct)
    {
        var window = new SettingsWindow(viewModel);
        window.Activate();
        try
        {
            await UiWait.UntilAsync(
                () => window.View.IsLoaded && VisualTree.Descendants(window.View).OfType<SettingsCard>().Any(),
                () => "The Settings window never loaded its cards.", ct);
        }
        catch
        {
            // A window left open keeps the test app from exiting.
            window.Close();
            throw;
        }

        return window;
    }

    private static T Named<T>(SettingsWindow window, string name) where T : FrameworkElement =>
        VisualTree.Descendants(window.View).OfType<T>().SingleOrDefault(e => AutomationProperties.GetName(e) == name)
        ?? throw new InvalidOperationException($"Settings has no {typeof(T).Name} named '{name}'.");

    private static void Click(Button button) => new ButtonAutomationPeer(button).Invoke();
}
