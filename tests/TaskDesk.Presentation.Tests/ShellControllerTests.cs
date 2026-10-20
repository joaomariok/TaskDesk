using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation.Tests;

public sealed class ShellControllerTests
{
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

    private readonly FakeTray _tray = new();
    private readonly FakeWindow _window = new();
    private readonly FakeSingleInstance _single = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeRunKey _runKey = new();
    private readonly FakeLifetime _lifetime = new();
    private readonly FakeAppUpdates _updates = new();
    private const string Exe = @"C:\TaskDesk\TaskDesk.exe";

    private ShellController Create() =>
        new(_tray, _window, _single, _settings, new AutoStartService(_runKey), _lifetime, updates: _updates);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

    // Started with an update check that is downloading and ignores cancellation, as a busy file write can.
    private async Task<(ShellController Controller, UpdateScheduler Checks, TaskCompletionSource Download)>
        StartedWithRunningCheckAsync(ShellExitParts? parts = null)
    {
        var download = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _updates.Gate = download;
        _updates.IgnoresCancellation = true;
        var checks = new UpdateScheduler(_updates, RuntimeMode.Normal, _clock);
        checks.Start();
        await _updates.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var controller = new ShellController(
            _tray, _window, _single, _settings, new AutoStartService(_runKey), _lifetime, updates: _updates,
            updateChecks: checks);
        if (parts is null)
        {
            await controller.StartAsync(false, Exe, TestContext.Current.CancellationToken);
        }
        else
        {
            await controller.LaunchAsync(Launch(parts), TestContext.Current.CancellationToken);
        }

        return (controller, checks, download);
    }

    private async Task<ShellController> StartedAsync(bool minimized = false)
    {
        var controller = Create();
        await controller.StartAsync(minimized, Exe, TestContext.Current.CancellationToken);
        return controller;
    }

    private static readonly Func<Task> NoPart = () => Task.CompletedTask;

    private static Func<Task> Part(Action action) => () =>
    {
        action();
        return Task.CompletedTask;
    };

    private static ShellExitParts Parts(
        Func<Task>? prepareWindow = null,
        Func<Task>? stopHost = null,
        Func<Task>? stopSystemHooks = null,
        Func<Task>? releaseHotkey = null,
        Func<Task>? disposeHost = null) => new()
        {
            PrepareWindow = prepareWindow ?? NoPart,
            StopHost = stopHost ?? NoPart,
            StopSystemHooks = stopSystemHooks ?? NoPart,
            ReleaseHotkey = releaseHotkey ?? NoPart,
            DisposeHost = disposeHost ?? NoPart,
        };

    private static ShellLaunch Launch(
        ShellExitParts? parts = null,
        bool minimized = false,
        Func<CancellationToken, Task>? activation = null,
        Action? running = null,
        Action? startSystemHooks = null) => new()
        {
            Minimized = minimized,
            ExePath = Exe,
            ExitParts = parts ?? Parts(),
            StartSystemHooks = startSystemHooks ?? (() => { }),
            Activation = activation,
            Running = running ?? (() => { }),
        };

    // Launched with the given exit parts, no launch activation and no background work.
    private async Task<ShellController> LaunchedAsync(ShellExitParts parts, bool minimized = false)
    {
        var controller = Create();
        await controller.LaunchAsync(Launch(parts, minimized), TestContext.Current.CancellationToken);
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

    // The launch gate finds the running copy (LaunchGateTests); a start without the guard is a start-up bug.
    [Fact]
    public async Task Start_without_the_single_instance_guard_is_rejected_and_signals_no_one()
    {
        _single.IsFirst = false;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Create().StartAsync(false, Exe, TestContext.Current.CancellationToken));

        Assert.Equal(0, _single.SignalCount);
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
        var flush = new TaskCompletionSource();
        var controller = await LaunchedAsync(Parts(prepareWindow: () => flush.Task), minimized: true);
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
        var log = new List<string>();
        var flush = new TaskCompletionSource();
        var controller = await LaunchedAsync(Parts(
            prepareWindow: async () =>
            {
                log.Add("flush started");
                await flush.Task;
                log.Add("flush done");
            },
            stopHost: () =>
            {
                log.Add("stop host");
                return Task.CompletedTask;
            }));

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
    public async Task Restart_to_update_runs_the_exit_steps_then_applies_the_update()
    {
        var flush = new TaskCompletionSource();
        var restartsDuringStep = -1;
        var controller = await LaunchedAsync(Parts(prepareWindow: async () =>
        {
            await flush.Task;
            restartsDuringStep = _updates.Restarts;
        }));

        var restart = controller.RestartToUpdateAsync();
        Assert.Equal(0, _updates.Restarts);

        flush.SetResult();
        await restart;

        Assert.Equal(0, restartsDuringStep);
        Assert.Equal(1, _updates.Restarts);
        Assert.Equal(1, _tray.DisposeCount);
    }

    [Fact]
    public async Task Tray_restart_to_update_runs_the_exit_then_applies_the_update()
    {
        var steps = 0;
        var controller = await LaunchedAsync(Parts(prepareWindow: () =>
        {
            steps++;
            return Task.CompletedTask;
        }));

        _tray.ClickRestartToUpdate();
        await controller.ExitAsync();

        Assert.Equal(1, steps);
        Assert.Equal(1, _updates.Restarts);
    }

    // The restarted process must not apply at start: when applying failed, the update is still pending (review M3).
    [Fact]
    public async Task Restart_to_update_marks_the_restart_as_the_updater_s()
    {
        var controller = await StartedAsync();

        await controller.RestartToUpdateAsync();

        Assert.Equal([LaunchGate.RestartedFlag], _updates.RestartArguments);
    }

    // The apply must not race a download still writing to, or cleaning, the packages folder (review M2).
    [Fact]
    public async Task Restart_to_update_applies_once_the_running_check_has_ended()
    {
        var (controller, _, download) = await StartedWithRunningCheckAsync();

        var restart = controller.RestartToUpdateAsync();
        _clock.Advance(UpdateScheduler.StopTimeout - TimeSpan.FromMilliseconds(1));
        Assert.False(restart.IsCompleted);
        Assert.Equal(0, _updates.Restarts);

        download.SetResult();
        await restart.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _updates.Restarts);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    // The update stays pending and applies at the next start (review M4).
    [Fact]
    public async Task Restart_to_update_exits_without_applying_when_the_check_does_not_end_in_time()
    {
        var (controller, _, download) = await StartedWithRunningCheckAsync();

        var restart = controller.RestartToUpdateAsync();
        _clock.Advance(UpdateScheduler.StopTimeout);
        await restart.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, _updates.Restarts);
        Assert.Equal(1, _lifetime.ShutdownCount);
        download.SetResult();
    }

    // The owner asked for a restart: TaskDesk starts again on the current version, so Reminders keep running, and
    // the update applies at its next normal start (review M5).
    [Fact]
    public async Task Restart_to_update_restarts_the_current_version_when_the_check_does_not_end_in_time()
    {
        var (controller, _, download) = await StartedWithRunningCheckAsync();
        var guardReleasedFirst = false;
        _updates.Relaunching = () => guardReleasedFirst = _single.DisposeCount > 0;

        var restart = controller.RestartToUpdateAsync();
        _clock.Advance(UpdateScheduler.StopTimeout);
        await restart.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _updates.Relaunches);
        Assert.Equal([LaunchGate.RestartedFlag], _updates.RelaunchArguments);
        Assert.True(guardReleasedFirst);
        Assert.Equal(1, _lifetime.ShutdownCount);
        download.SetResult();
    }

    // The timed wait gave up during the exit steps, but the download ended before they did (review round 3, low).
    [Fact]
    public async Task Restart_to_update_applies_when_the_check_ends_during_the_exit_steps()
    {
        var flush = new TaskCompletionSource();
        var (controller, checks, download) = await StartedWithRunningCheckAsync(Parts(prepareWindow: () => flush.Task));

        var restart = controller.RestartToUpdateAsync();
        _clock.Advance(UpdateScheduler.StopTimeout);
        download.SetResult();
        await checks.CurrentCheck.WaitAsync(TestContext.Current.CancellationToken);
        flush.SetResult();
        await restart.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _updates.Restarts);
        Assert.Equal(0, _updates.Relaunches);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task Restart_to_update_that_applies_does_not_also_restart_the_current_version()
    {
        var controller = await StartedAsync();

        await controller.RestartToUpdateAsync();

        Assert.Equal(1, _updates.Restarts);
        Assert.Equal(0, _updates.Relaunches);
    }

    [Fact]
    public async Task Exit_does_not_restart_the_app()
    {
        var (controller, _, download) = await StartedWithRunningCheckAsync();

        await controller.ExitAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(0, _updates.Relaunches);
        download.SetResult();
    }

    // Only Restart to update waits: a plain exit or a session end just cancels the check.
    [Fact]
    public async Task Exit_cancels_the_running_check_without_waiting_for_it()
    {
        var (controller, checks, download) = await StartedWithRunningCheckAsync();

        await controller.ExitAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(1, _lifetime.ShutdownCount);
        Assert.Equal(0, _updates.Restarts);
        download.SetResult();
        await checks.CurrentCheck;
        _clock.Advance(UpdateScheduler.Interval);
        await checks.CurrentCheck;
        Assert.Equal(1, _updates.Checks);
    }

    [Fact]
    public async Task Restart_to_update_once_an_exit_has_started_only_exits()
    {
        var flush = new TaskCompletionSource();
        var controller = await LaunchedAsync(Parts(prepareWindow: () => flush.Task));
        var exit = controller.ExitAsync();

        var restart = controller.RestartToUpdateAsync();
        flush.SetResult();
        await restart;
        await exit;

        Assert.Equal(0, _updates.Restarts);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task Exit_does_not_apply_the_update()
    {
        var controller = await StartedAsync();

        await controller.ExitAsync();

        Assert.Equal(0, _updates.Restarts);
    }

    [Fact]
    public async Task Tray_exit_waits_for_the_exit_steps()
    {
        var flush = new TaskCompletionSource();
        var controller = await LaunchedAsync(Parts(prepareWindow: () => flush.Task));

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
        var flush = new TaskCompletionSource();
        var controller = await LaunchedAsync(Parts(prepareWindow: () => flush.Task));

        controller.OnWindowClosing();
        Assert.Equal(0, _lifetime.ShutdownCount);

        flush.SetResult();
        await controller.ExitAsync();

        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task A_failing_exit_step_does_not_stop_later_steps_or_the_shutdown()
    {
        var ran = false;
        var controller = await LaunchedAsync(Parts(
            prepareWindow: () => throw new InvalidOperationException("boom"),
            stopHost: () =>
            {
                ran = true;
                return Task.CompletedTask;
            }));

        await controller.ExitAsync();

        Assert.True(ran);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task Parts_handed_again_after_the_exit_started_are_rejected_and_the_exit_still_shuts_down()
    {
        var flush = new TaskCompletionSource();
        var controller = await LaunchedAsync(Parts(prepareWindow: () => flush.Task));
        var exit = controller.ExitAsync();

        var late = await Record.ExceptionAsync(
            () => controller.LaunchAsync(Launch(), TestContext.Current.CancellationToken));
        flush.SetResult();
        await exit;

        Assert.IsType<InvalidOperationException>(late);
        Assert.Equal(1, _tray.DisposeCount);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task Exit_requested_twice_runs_the_steps_once()
    {
        var runs = 0;
        var controller = await LaunchedAsync(Parts(prepareWindow: () =>
        {
            runs++;
            return Task.CompletedTask;
        }));

        var first = controller.ExitAsync();
        var second = controller.ExitAsync();
        await Task.WhenAll(first, second);

        Assert.Equal(1, runs);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task A_launch_starts_the_shell_acts_on_its_activation_then_starts_the_background_work()
    {
        var controller = Create();
        var steps = new List<string>();

        var launched = await controller.LaunchAsync(
            Launch(
                minimized: false,
                activation: cancellationToken =>
                {
                    steps.Add(cancellationToken.IsCancellationRequested ? "activation (cancelled)" : "activation");
                    return Task.CompletedTask;
                },
                running: () => steps.Add("running")),
            TestContext.Current.CancellationToken);

        Assert.True(launched);
        Assert.True(_tray.Shown);
        Assert.Equal(["activation", "running"], steps);
    }

    [Fact]
    public async Task An_exit_started_while_the_launch_activation_runs_cancels_it_and_skips_the_background_work()
    {
        var controller = Create();
        var acting = new TaskCompletionSource();
        var activationToken = CancellationToken.None;
        var running = 0;
        var launch = controller.LaunchAsync(
            Launch(
                minimized: true,
                activation: cancellationToken =>
                {
                    activationToken = cancellationToken;
                    return acting.Task;
                },
                running: () => running++),
            TestContext.Current.CancellationToken);

        // The tray's Exit, or session end, while the activation still runs.
        _tray.ClickExit();
        Assert.True(activationToken.IsCancellationRequested);
        acting.SetResult();
        await launch;
        // The exit waited for the activation, so it may finish just after the launch does.
        await controller.ExitAsync();

        Assert.Equal(0, running);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task A_launch_activation_whose_cancellation_throws_does_not_stop_the_exit()
    {
        var controller = Create();
        var acting = new TaskCompletionSource();
        var ran = false;
        var launch = controller.LaunchAsync(
            Launch(
                Parts(prepareWindow: () =>
                {
                    ran = true;
                    return Task.CompletedTask;
                }),
                minimized: true,
                activation: cancellationToken =>
                {
                    cancellationToken.Register(() => throw new InvalidOperationException("boom"));
                    return acting.Task;
                },
                running: () => { }),
            TestContext.Current.CancellationToken);

        // The exit waits for the activation before its steps, so the activation ends before the exit is awaited.
        var exit = controller.ExitAsync();
        acting.SetResult();
        await exit;
        await launch;

        Assert.True(ran);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task An_exit_started_while_the_launch_activation_runs_waits_for_it_before_the_exit_steps()
    {
        var controller = Create();
        var acting = new TaskCompletionSource();
        var steps = new List<string>();
        var launch = controller.LaunchAsync(
            Launch(
                Parts(prepareWindow: () =>
                {
                    steps.Add("flush");
                    return Task.CompletedTask;
                }),
                minimized: true,
                activation: async _ =>
                {
                    // A toast's Mark done: its write must land before the flush and the host stop.
                    await acting.Task;
                    steps.Add("activation");
                },
                running: () => steps.Add("running")),
            TestContext.Current.CancellationToken);

        var exit = controller.ExitAsync();
        Assert.Empty(steps);
        Assert.Equal(0, _lifetime.ShutdownCount);

        acting.SetResult();
        await exit;
        await launch;

        Assert.Equal(["activation", "flush"], steps);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task The_exit_waits_for_an_activation_routed_in_the_running_app_before_its_steps()
    {
        var acting = new TaskCompletionSource();
        var steps = new List<string>();
        var controller = await LaunchedAsync(Parts(prepareWindow: () =>
        {
            steps.Add("flush");
            return Task.CompletedTask;
        }));
        // A toast's Mark done clicked as the exit starts: its write must land before the flush and the host stop.
        var activation = controller.RunActivationAsync(async exitStarted =>
        {
            await acting.Task;
            steps.Add(exitStarted.IsCancellationRequested ? "activation (exit started)" : "activation");
        });

        var exit = controller.ExitAsync();
        Assert.Empty(steps);
        Assert.Equal(0, _lifetime.ShutdownCount);

        acting.SetResult();
        await exit;
        await activation;

        Assert.Equal(["activation (exit started)", "flush"], steps);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task The_exit_also_waits_for_an_activation_routed_while_it_waits_for_another()
    {
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        var steps = new List<string>();
        var controller = await LaunchedAsync(Parts(prepareWindow: () =>
        {
            steps.Add("flush");
            return Task.CompletedTask;
        }));
        var firstActivation = controller.RunActivationAsync(async _ =>
        {
            await first.Task;
            steps.Add("first");
        });
        var exit = controller.ExitAsync();
        var secondActivation = controller.RunActivationAsync(async _ =>
        {
            await second.Task;
            steps.Add("second");
        });

        first.SetResult();
        await firstActivation;
        Assert.Equal(["first"], steps);
        second.SetResult();
        await exit;
        await secondActivation;

        Assert.Equal(["first", "second", "flush"], steps);
    }

    [Fact]
    public async Task An_activation_runs_without_holding_up_one_routed_from_another_thread()
    {
        var controller = await StartedAsync();
        var nestedRan = false;
        var nestedReturned = false;

        // While the first activation's synchronous part runs, a toast click forwarded on another thread routes one
        // more. Starting it must not wait for the first to yield.
        await controller.RunActivationAsync(_ =>
        {
            var other = new Thread(() => controller.RunActivationAsync(_ =>
            {
                nestedRan = true;
                return Task.CompletedTask;
            }));
            other.Start();
            nestedReturned = other.Join(TimeSpan.FromSeconds(2));
            return Task.CompletedTask;
        });

        Assert.True(nestedReturned, "Routing an activation waited for another one's synchronous part.");
        Assert.True(nestedRan);
    }

    [Fact]
    public async Task An_activation_routed_once_the_exit_steps_have_started_is_dropped()
    {
        var flush = new TaskCompletionSource();
        var controller = await LaunchedAsync(Parts(prepareWindow: () => flush.Task));
        var exit = controller.ExitAsync();
        var ran = false;

        // The flush and the host stop are under way: the activation would write to a host being stopped.
        await controller.RunActivationAsync(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        });
        flush.SetResult();
        await exit;

        Assert.False(ran);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task A_failing_launch_activation_still_starts_the_background_work()
    {
        var controller = Create();
        var running = 0;

        var launched = await controller.LaunchAsync(
            Launch(
                minimized: false,
                activation: _ => throw new InvalidOperationException("boom"),
                running: () => running++),
            TestContext.Current.CancellationToken);

        Assert.True(launched);
        Assert.Equal(1, running);
    }

    [Fact]
    public async Task A_launch_activation_that_fails_while_the_exit_waits_for_it_does_not_stop_the_exit()
    {
        var controller = Create();
        var acting = new TaskCompletionSource();
        var ran = false;
        var launch = controller.LaunchAsync(
            Launch(
                Parts(prepareWindow: () =>
                {
                    ran = true;
                    return Task.CompletedTask;
                }),
                minimized: true,
                activation: async _ =>
                {
                    await acting.Task;
                    throw new InvalidOperationException("boom");
                },
                running: () => { }),
            TestContext.Current.CancellationToken);

        var exit = controller.ExitAsync();
        acting.SetResult();
        await exit;
        await launch;

        Assert.True(ran);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task The_exit_runs_the_named_parts_in_order_after_the_activations_and_before_the_shutdown()
    {
        var log = new List<string>();
        Func<Task> Logged(string name) => Part(() =>
        {
            var shellReleased = _tray.DisposeCount == 1 && _single.DisposeCount == 1;
            log.Add(shellReleased ? $"{name} (shell released)" : name);
            if (_lifetime.ShutdownCount > 0)
            {
                log.Add("shut down already");
            }
        });
        var controller = await LaunchedAsync(new ShellExitParts
        {
            PrepareWindow = Logged("prepare window"),
            StopHost = Logged("stop host"),
            StopSystemHooks = Logged("stop system hooks"),
            ReleaseHotkey = Logged("release hotkey"),
            DisposeHost = Logged("dispose host"),
        });
        var acting = new TaskCompletionSource();
        var activation = controller.RunActivationAsync(async _ =>
        {
            await acting.Task;
            log.Add("activation");
        });

        var exit = controller.ExitAsync();
        acting.SetResult();
        await exit;
        await activation;

        Assert.Equal(
            ["activation", "prepare window", "stop host", "stop system hooks", "release hotkey",
                "dispose host (shell released)"],
            log);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task Every_exit_part_runs_although_each_one_fails()
    {
        var ran = new List<string>();
        Func<Task> Failing(string name) => () =>
        {
            ran.Add(name);
            throw new InvalidOperationException(name);
        };
        var controller = await LaunchedAsync(new ShellExitParts
        {
            PrepareWindow = async () =>
            {
                await Task.Yield();
                ran.Add("prepare window");
                throw new InvalidOperationException("prepare window");
            },
            StopHost = Failing("stop host"),
            StopSystemHooks = Failing("stop system hooks"),
            ReleaseHotkey = Failing("release hotkey"),
            DisposeHost = Failing("dispose host"),
        });

        await controller.ExitAsync();

        Assert.Equal(["prepare window", "stop host", "stop system hooks", "release hotkey", "dispose host"], ran);
        Assert.Equal(1, _tray.DisposeCount);
        Assert.Equal(1, _single.DisposeCount);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task An_exit_started_from_the_system_hooks_during_the_launch_still_runs_every_part()
    {
        var controller = Create();
        var ran = new List<string>();
        var running = 0;

        // Session end arrives as soon as its hook is in place.
        await controller.LaunchAsync(
            Launch(
                Parts(
                    Part(() => ran.Add("prepare window")),
                    Part(() => ran.Add("stop host")),
                    Part(() => ran.Add("stop system hooks")),
                    Part(() => ran.Add("release hotkey")),
                    Part(() => ran.Add("dispose host"))),
                running: () => running++,
                startSystemHooks: () => controller.Exit()),
            TestContext.Current.CancellationToken);
        await controller.ExitAsync();

        Assert.Equal(["prepare window", "stop host", "stop system hooks", "release hotkey", "dispose host"], ran);
        Assert.Equal(0, running);
        Assert.False(_window.IsVisible);
        Assert.Equal(1, _tray.DisposeCount);
        Assert.Equal(1, _single.DisposeCount);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task The_launch_starts_the_system_hooks_before_the_tray_and_the_window()
    {
        var controller = Create();
        bool? trayShown = null;
        bool? windowShown = null;

        await controller.LaunchAsync(
            Launch(startSystemHooks: () =>
            {
                trayShown = _tray.Shown;
                windowShown = _window.IsVisible;
            }),
            TestContext.Current.CancellationToken);

        Assert.False(trayShown);
        Assert.False(windowShown);
        Assert.True(_tray.Shown);
        Assert.True(_window.IsVisible);
    }

    [Fact]
    public async Task The_tray_and_the_guard_are_released_once()
    {
        var controller = await LaunchedAsync(Parts());

        _tray.ClickExit();
        await controller.ExitAsync();
        controller.Dispose();

        Assert.Equal(1, _tray.DisposeCount);
        Assert.Equal(1, _single.DisposeCount);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task Parts_handed_twice_are_rejected_and_the_first_parts_run_once()
    {
        var first = 0;
        var second = 0;
        var hooksStarted = 0;
        var controller = Create();
        await controller.LaunchAsync(
            Launch(Parts(Part(() => first++)), startSystemHooks: () => hooksStarted++),
            TestContext.Current.CancellationToken);

        var again = await Record.ExceptionAsync(() => controller.LaunchAsync(
            Launch(Parts(Part(() => second++)), startSystemHooks: () => hooksStarted++),
            TestContext.Current.CancellationToken));
        await controller.ExitAsync();

        Assert.IsType<InvalidOperationException>(again);
        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(1, hooksStarted);
        Assert.Equal(1, _tray.DisposeCount);
        Assert.Equal(1, _lifetime.ShutdownCount);
    }

    [Fact]
    public async Task Parts_handed_after_the_exit_started_are_rejected_and_never_run()
    {
        var controller = Create();
        var ran = 0;
        var hooksStarted = 0;
        var exit = controller.ExitAsync();

        var late = await Record.ExceptionAsync(() => controller.LaunchAsync(
            Launch(Parts(Part(() => ran++)), startSystemHooks: () => hooksStarted++),
            TestContext.Current.CancellationToken));
        await exit;

        Assert.IsType<InvalidOperationException>(late);
        Assert.Equal(0, ran);
        Assert.Equal(0, hooksStarted);
        Assert.False(_tray.Shown);
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
