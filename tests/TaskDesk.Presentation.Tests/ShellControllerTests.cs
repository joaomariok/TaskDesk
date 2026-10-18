using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation.Tests;

public sealed class ShellControllerTests
{
    private sealed class FakeTray : ITrayIcon
    {
        public event Action? LeftClicked;
        public event Action? OpenRequested;
        public event Action? QuickAddRequested;
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
        public void ClickQuickAdd() => QuickAddRequested?.Invoke();
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
        public List<IReadOnlyList<string>> Signals { get; } = [];
        public int DisposeCount { get; private set; }
        public Action<IReadOnlyList<string>>? OnSecondLaunch { get; private set; }

        public bool TryAcquire() => IsFirst;
        public Task SignalExistingAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            SignalCount++;
            Signals.Add(arguments);
            return Task.CompletedTask;
        }

        public void Listen(Action<IReadOnlyList<string>> onSecondLaunch) => OnSecondLaunch = onSecondLaunch;
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

        _single.OnSecondLaunch!([]);

        Assert.True(_window.IsVisible);
        Assert.Equal(1, _window.FocusCount);
    }

    [Fact]
    public async Task A_later_launch_from_a_Reminder_notification_raises_the_activation_without_showing_the_window()
    {
        var controller = await StartedAsync(minimized: true);
        var activations = new List<NotificationActivation>();
        controller.ActivationRequested += activations.Add;
        var markDone = new NotificationActivation(Guid.NewGuid(), ReminderAction.MarkDone);

        _single.OnSecondLaunch!(SecondLaunchArguments.Build(minimized: true, markDone));

        Assert.Equal([markDone], activations);
        Assert.False(_window.IsVisible);
    }

    [Fact]
    public async Task A_later_minimized_launch_leaves_the_window_hidden()
    {
        await StartedAsync(minimized: true);

        _single.OnSecondLaunch!([SecondLaunchArguments.MinimizedFlag]);

        Assert.False(_window.IsVisible);
        Assert.Equal(0, _window.FocusCount);
    }

    [Fact]
    public async Task A_later_launch_while_exiting_is_ignored()
    {
        var controller = await StartedAsync(minimized: true);
        var flush = new TaskCompletionSource();
        controller.AddExitStep(() => flush.Task);
        var activations = 0;
        controller.ActivationRequested += _ => activations++;
        var exit = controller.ExitAsync();

        _single.OnSecondLaunch!([]);
        _single.OnSecondLaunch!(SecondLaunchArguments.Build(false, NotificationActivation.OpenToday));
        flush.SetResult();
        await exit;

        Assert.Equal(0, _window.FocusCount);
        Assert.Equal(0, activations);
    }

    [Fact]
    public async Task A_second_instance_forwards_its_minimized_flag()
    {
        _single.IsFirst = false;

        await Create().StartAsync(minimized: true, Exe, TestContext.Current.CancellationToken);

        Assert.Equal([SecondLaunchArguments.MinimizedFlag], Assert.Single(_single.Signals));
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
        _runKey.Values["TaskDesk"] = "\"C:\\TaskDesk\\TaskDesk.exe\" --minimized";

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

    [Fact]
    public async Task Close_with_exit_behavior_shuts_down_instead_of_hiding()
    {
        _settings.Current = new ShellSettings(CloseBehavior: CloseBehavior.Exit);
        var controller = await StartedAsync();

        var cancel = controller.OnWindowClosing();

        Assert.False(cancel);
        Assert.True(_window.IsVisible);
        Assert.Equal(1, _lifetime.ShutdownCount);
        Assert.Empty(_tray.Hints);
    }

    [Fact]
    public async Task Quick_add_menu_item_raises_quick_add_requested()
    {
        var controller = await StartedAsync();
        var raised = 0;
        controller.QuickAddRequested += () => raised++;

        _tray.ClickQuickAdd();

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Exit_runs_exit_steps_in_order_and_shuts_down_only_after_they_finish()
    {
        var controller = await StartedAsync();
        var log = new List<string>();
        var flush = new TaskCompletionSource();
        controller.AddExitStep(async () =>
        {
            log.Add("flush started");
            await flush.Task;
            log.Add("flush done");
        });
        controller.AddExitStep(() =>
        {
            log.Add("stop host");
            return Task.CompletedTask;
        });

        var exit = controller.ExitAsync();

        Assert.Equal(["flush started"], log);
        Assert.Equal(0, _lifetime.ShutdownCount);
        Assert.Equal(0, _tray.DisposeCount);

        flush.SetResult();
        await exit;

        Assert.Equal(["flush started", "flush done", "stop host"], log);
        Assert.Equal(1, _tray.DisposeCount);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task Tray_exit_waits_for_the_exit_steps()
    {
        var controller = await StartedAsync();
        var flush = new TaskCompletionSource();
        controller.AddExitStep(() => flush.Task);

        _tray.ClickExit();
        Assert.Equal(0, _lifetime.ShutdownCount);

        flush.SetResult();
        await controller.ExitAsync();

        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task Close_with_exit_behavior_waits_for_the_exit_steps()
    {
        _settings.Current = new ShellSettings(CloseBehavior: CloseBehavior.Exit);
        var controller = await StartedAsync();
        var flush = new TaskCompletionSource();
        controller.AddExitStep(() => flush.Task);

        controller.OnWindowClosing();
        Assert.Equal(0, _lifetime.ShutdownCount);

        flush.SetResult();
        await controller.ExitAsync();

        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task A_failing_exit_step_does_not_stop_later_steps_or_the_shutdown()
    {
        var controller = await StartedAsync();
        var ran = false;
        controller.AddExitStep(() => throw new InvalidOperationException("boom"));
        controller.AddExitStep(() =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        await controller.ExitAsync();

        Assert.True(ran);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task An_exit_step_added_after_the_exit_started_is_rejected_and_the_exit_still_shuts_down()
    {
        var controller = await StartedAsync();
        var flush = new TaskCompletionSource();
        controller.AddExitStep(() => flush.Task);
        var exit = controller.ExitAsync();

        var late = Record.Exception(() => controller.AddExitStep(() => Task.CompletedTask));
        flush.SetResult();
        await exit;

        Assert.IsType<InvalidOperationException>(late);
        Assert.Equal(1, _tray.DisposeCount);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task Exit_requested_twice_runs_the_steps_once()
    {
        var controller = await StartedAsync();
        var runs = 0;
        controller.AddExitStep(() =>
        {
            runs++;
            return Task.CompletedTask;
        });

        var first = controller.ExitAsync();
        var second = controller.ExitAsync();
        await Task.WhenAll(first, second);

        Assert.Equal(1, runs);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task Quick_add_requests_stop_after_dispose()
    {
        var controller = await StartedAsync();
        var raised = 0;
        controller.QuickAddRequested += () => raised++;
        controller.Dispose();

        _tray.ClickQuickAdd();

        Assert.Equal(0, raised);
    }
}
