using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation.Tests;

public sealed class ShellControllerTests
{
    private sealed class FakeTray : ITrayIcon
    {
        public event Action? LeftClicked;
        public event Action? OpenRequested;
        public event Action? SettingsRequested;
        public event Action? ExitRequested;

        public bool Shown { get; private set; }
        public int DisposeCount { get; private set; }
        public List<string> Hints { get; } = [];

        public void Show() => Shown = true;
        public void ShowHint(string message) => Hints.Add(message);
        public void Dispose() => DisposeCount++;

        public void ClickLeft() => LeftClicked?.Invoke();
        public void ClickOpen() => OpenRequested?.Invoke();
        public void ClickSettings() => SettingsRequested?.Invoke();
        public void ClickExit() => ExitRequested?.Invoke();
    }

    private sealed class FakeWindow : IMainWindow
    {
        public bool IsVisible { get; private set; }
        public int FocusCount { get; private set; }

        public void ShowAndFocus()
        {
            IsVisible = true;
            FocusCount++;
        }

        public void Hide() => IsVisible = false;
    }

    private sealed class FakeSingleInstance : ISingleInstance
    {
        public bool IsFirst { get; set; } = true;
        public int SignalCount { get; private set; }
        public int DisposeCount { get; private set; }
        public Action? OnSecondLaunch { get; private set; }

        public bool TryAcquire() => IsFirst;
        public Task SignalExistingAsync(CancellationToken cancellationToken)
        {
            SignalCount++;
            return Task.CompletedTask;
        }

        public void Listen(Action onSecondLaunch) => OnSecondLaunch = onSecondLaunch;
        public void Dispose() => DisposeCount++;
    }

    private sealed class FakeSettings : IShellSettingsStore
    {
        public ShellSettings Current { get; set; } = new();
        public ShellSettings Load() => Current;
        public void Save(ShellSettings settings) => Current = settings;
    }

    private sealed class FakeRunKey : IRunKey
    {
        public Dictionary<string, string> Values { get; } = [];
        public string? GetValue(string name) => Values.GetValueOrDefault(name);
        public void SetValue(string name, string value) => Values[name] = value;
        public void Remove(string name) => Values.Remove(name);
    }

    private sealed class FakeLifetime : IAppShutdown
    {
        public int ShutdownCount { get; private set; }
        public void Shutdown() => ShutdownCount++;
    }

    private readonly FakeTray _tray = new();
    private readonly FakeWindow _window = new();
    private readonly FakeSingleInstance _single = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeRunKey _runKey = new();
    private readonly FakeLifetime _lifetime = new();
    private const string Exe = @"C:\TaskDesk\TaskDesk.exe";

    private ShellController Create() =>
        new(_tray, _window, _single, _settings, new AutoStartService(_runKey), _lifetime);

    private async Task<ShellController> StartedAsync(bool minimized = false)
    {
        var controller = Create();
        await controller.StartAsync(minimized, Exe, TestContext.Current.CancellationToken);
        return controller;
    }

    [Fact]
    public async Task Start_shows_tray_and_window()
    {
        await StartedAsync();

        Assert.True(_tray.Shown);
        Assert.True(_window.IsVisible);
    }

    [Fact]
    public async Task Start_minimized_keeps_window_hidden_but_tray_present()
    {
        await StartedAsync(minimized: true);

        Assert.True(_tray.Shown);
        Assert.False(_window.IsVisible);
    }

    [Fact]
    public async Task Second_instance_signals_the_first_and_does_not_set_up()
    {
        _single.IsFirst = false;
        var controller = Create();

        var started = await controller.StartAsync(false, Exe, TestContext.Current.CancellationToken);

        Assert.False(started);
        Assert.Equal(1, _single.SignalCount);
        Assert.False(_tray.Shown);
        Assert.Empty(_runKey.Values);
    }

    [Fact]
    public async Task Signal_from_a_later_launch_shows_and_focuses_the_window()
    {
        await StartedAsync(minimized: true);

        _single.OnSecondLaunch!();

        Assert.True(_window.IsVisible);
        Assert.Equal(1, _window.FocusCount);
    }

    [Fact]
    public async Task Left_click_toggles_window_visibility()
    {
        await StartedAsync(minimized: true);

        _tray.ClickLeft();
        Assert.True(_window.IsVisible);

        _tray.ClickLeft();
        Assert.False(_window.IsVisible);
    }

    [Fact]
    public async Task Open_menu_item_shows_window()
    {
        await StartedAsync(minimized: true);

        _tray.ClickOpen();

        Assert.True(_window.IsVisible);
    }

    [Fact]
    public async Task Settings_menu_item_raises_settings_requested()
    {
        var controller = await StartedAsync();
        var raised = 0;
        controller.SettingsRequested += () => raised++;

        _tray.ClickSettings();

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Closing_hides_to_tray_and_cancels_the_close()
    {
        var controller = await StartedAsync();

        var cancel = controller.OnWindowClosing();

        Assert.True(cancel);
        Assert.False(_window.IsVisible);
    }

    [Fact]
    public async Task Tray_hint_is_shown_only_the_first_time()
    {
        var controller = await StartedAsync();

        controller.OnWindowClosing();
        _tray.ClickLeft();
        controller.OnWindowClosing();

        Assert.Equal([ShellController.TrayHint], _tray.Hints);
        Assert.True(_settings.Current.TrayHintShown);
    }

    [Fact]
    public async Task Tray_hint_is_not_repeated_when_already_seen()
    {
        _settings.Current = new ShellSettings(TrayHintShown: true);
        var controller = await StartedAsync();

        controller.OnWindowClosing();

        Assert.Empty(_tray.Hints);
    }

    [Fact]
    public async Task Start_writes_run_key_with_current_exe_path_by_default()
    {
        await StartedAsync();

        Assert.Equal("\"C:\\TaskDesk\\TaskDesk.exe\" --minimized", _runKey.Values["TaskDesk"]);
    }

    [Fact]
    public async Task Start_rewrites_a_stale_run_key_path()
    {
        _runKey.Values["TaskDesk"] = "\"C:\\Old\\TaskDesk.exe\" --minimized";

        await StartedAsync();

        Assert.Equal("\"C:\\TaskDesk\\TaskDesk.exe\" --minimized", _runKey.Values["TaskDesk"]);
    }

    [Fact]
    public async Task Start_removes_run_key_when_setting_is_off()
    {
        _settings.Current = new ShellSettings(StartWithWindows: false);
        _runKey.Values["TaskDesk"] = "x";

        await StartedAsync();

        Assert.Empty(_runKey.Values);
    }

    [Fact]
    public async Task Exit_disposes_tray_and_guard_then_shuts_down()
    {
        await StartedAsync();

        _tray.ClickExit();

        Assert.Equal(1, _tray.DisposeCount);
        Assert.Equal(1, _single.DisposeCount);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task Closing_while_exiting_is_not_cancelled()
    {
        var controller = await StartedAsync();
        controller.Exit();

        Assert.False(controller.OnWindowClosing());
    }

    [Fact]
    public async Task Exit_then_dispose_disposes_the_tray_once()
    {
        var controller = await StartedAsync();

        controller.Exit();
        controller.Dispose();

        Assert.Equal(1, _tray.DisposeCount);
    }
}
