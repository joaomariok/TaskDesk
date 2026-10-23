using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation;

/// <summary>
/// Ties the tray icon, main window, single-instance guard and auto-start together, runs the exit parts in a fixed
/// order (<see cref="ShellExitParts"/>), shows the tray's Restart to update while <see cref="UpdateScheduler"/> holds a
/// downloaded version, stops the update checks on exit, and has the <see cref="UpdateScheduler"/> restart after the
/// exit for Restart to update. A null <see cref="UpdateScheduler"/> means updates are absent.
/// </summary>
public sealed class ShellController(
    ITrayIcon tray,
    IMainWindow window,
    ISingleInstance singleInstance,
    IShellSettingsStore settingsStore,
    AutoStartService autoStart,
    IAppShutdown lifetime,
    ILogger<ShellController>? logger = null,
    UpdateScheduler? updates = null) : IDisposable
{
    public const string TrayHint = "TaskDesk is still running in the tray";

    private readonly ILogger _logger = logger ?? NullLogger<ShellController>.Instance;
    private readonly UpdateScheduler? _updates = updates;
    private ShellExitParts? _exitParts;
    private bool _launched;

    // Never disposed: a Reminder activation may still read its token after the exit, and it holds no timer.
    private readonly CancellationTokenSource _exitStarted = new();

    // Reminder activations still running, which the exit waits for; one can arrive on any thread.
    private readonly Lock _activationsGate = new();
    private readonly List<Task> _activations = [];
    private bool _activationsClosed;
    private Task? _exit;
    private bool _exiting;
    private bool _restartToUpdate;
    private bool _disposed;

    /// <summary>Raised by the tray menu's "Quick add"; the App opens the quick-add window.</summary>
    public event Action? QuickAddRequested;

    /// <summary>Raised by the tray menu; the App opens the Settings window in response.</summary>
    public event Action? SettingsRequested;

    /// <summary>
    /// Sets everything up in the process that owns the single-instance guard (<see cref="LaunchAsync"/> calls it): <see cref="LaunchGate"/> has claimed
    /// it and forwarded any launch that found another copy, so this only confirms ownership
    /// (<see cref="ISingleInstance.TryAcquire"/> keeps its first answer) and throws when the guard is held
    /// elsewhere. Listens for later launches, then sets up the Run key, the tray (its Restart to update follows the
    /// downloaded version from here on) and the window. Returns true.
    /// </summary>
    public Task<bool> StartAsync(bool minimized, string exePath, CancellationToken cancellationToken = default)
    {
        if (!singleInstance.TryAcquire())
        {
            throw new InvalidOperationException(
                "Another copy holds the single-instance guard; the launch gate forwards to it before the shell starts.");
        }

        singleInstance.Listen(OnSecondLaunch);
        autoStart.Apply(settingsStore.Load().StartWithWindows, exePath);

        tray.LeftClicked += OnLeftClicked;
        tray.OpenRequested += window.ShowAndFocus;
        tray.QuickAddRequested += OnQuickAddRequested;
        tray.SettingsRequested += OnSettingsRequested;
        tray.ExitRequested += Exit;
        tray.RestartToUpdateRequested += RestartToUpdate;
        tray.Show();
        if (_updates is not null)
        {
            // Subscribed before the read, so a version downloaded in between is not missed.
            _updates.PendingVersionChanged += OnPendingVersionChanged;
            OnPendingVersionChanged();
        }

        if (!minimized)
        {
            window.ShowAndFocus();
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// Cancelled when the exit starts. A Reminder activation reads it to skip what only a running app needs, such
    /// as showing the window; the action the owner chose (Mark done, Snooze) still completes.
    /// </summary>
    public CancellationToken ExitStarted => _exitStarted.Token;

    /// <summary>
    /// Start-up's last step. Records the exit parts, starts the system hooks (session end, resume), then
    /// <see cref="StartAsync"/> (the tray and the window), then the Reminder activation that launched the app, then
    /// the work that runs until the exit (such as the Reminder checks). From the hooks on, session end and the tray's
    /// Exit can start the exit, which runs every part. The activation gets <see cref="ExitStarted"/>, and the exit
    /// waits for it before its parts, so a Mark done or Snooze that launched the app is kept. A failing activation
    /// is logged and start-up carries on. Nothing more starts once an exit has started. Handing the parts twice, or
    /// after the exit started, throws <see cref="InvalidOperationException"/>. Returns true.
    /// </summary>
    public async Task<bool> LaunchAsync(ShellLaunch launch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (_exiting)
        {
            throw new InvalidOperationException("The exit has already started; hand the exit parts before it can start.");
        }

        if (_launched)
        {
            throw new InvalidOperationException("The exit parts have already been handed; the shell launches once.");
        }

        _launched = true;
        _exitParts = launch.ExitParts;
        launch.StartSystemHooks();
        if (_exiting)
        {
            return true;
        }

        await StartAsync(launch.Minimized, launch.ExePath, cancellationToken);

        if (launch.Activation is not null && !_exiting)
        {
            await RunActivationAsync(launch.Activation);
        }

        if (!_exiting)
        {
            launch.Running();
        }

        return true;
    }

    /// <summary>
    /// Runs a Reminder activation (the toast that launched the app, or a click on a toast or balloon in the running
    /// app) with <see cref="ExitStarted"/>. The exit waits for every activation still running before its parts, so
    /// a Mark done or Snooze clicked as the exit starts is kept. One that arrives once the exit parts have started
    /// is dropped: they stop and dispose what it would write to. Callable from any thread; never throws.
    /// </summary>
    public Task RunActivationAsync(Func<CancellationToken, Task> activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        // Registered under the gate, so the exit waits for it; started outside it, so its synchronous part never
        // holds up an activation routed from another thread.
        var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_activationsGate)
        {
            if (_activationsClosed)
            {
                _logger.LogInformation("A Reminder activation arrived during the exit and was dropped");
                return Task.CompletedTask;
            }

            _activations.RemoveAll(t => t.IsCompleted);
            _activations.Add(registered.Task);
        }

        return RunGuardedAsync(activation, registered);
    }

    // Never throws: the exit waits for it.
    private async Task RunGuardedAsync(Func<CancellationToken, Task> activation, TaskCompletionSource registered)
    {
        try
        {
            await activation(_exitStarted.Token);
        }
        catch (OperationCanceledException) when (_exitStarted.IsCancellationRequested)
        {
            _logger.LogDebug("A Reminder activation stopped for the exit");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "A Reminder activation failed");
        }
        finally
        {
            registered.TrySetResult();
        }
    }

    // Waits until no activation runs, then turns later ones away; one that arrives meanwhile is waited for too.
    private async Task WaitForActivationsAsync()
    {
        while (true)
        {
            Task[] running;
            lock (_activationsGate)
            {
                running = [.. _activations.Where(t => !t.IsCompleted)];
                if (running.Length == 0)
                {
                    _activationsClosed = true;
                    _activations.Clear();
                    return;
                }
            }

            await Task.WhenAll(running);
        }
    }

    /// <summary>Call from the window's Closing event; true means cancel the close (the window hides instead).</summary>
    public bool OnWindowClosing()
    {
        if (_exiting)
        {
            return false;
        }

        var settings = settingsStore.Load();
        if (settings.CloseBehavior == CloseBehavior.Exit)
        {
            Exit();
            return false;
        }

        window.Hide();
        if (!settings.TrayHintShown)
        {
            tray.ShowHint(TrayHint);
            settingsStore.Save(settings with { TrayHintShown = true });
        }

        return true;
    }

    /// <summary>Starts the restart without waiting for it; see <see cref="RestartToUpdateAsync"/>.</summary>
    public void RestartToUpdate() => _ = RestartToUpdateAsync();

    /// <summary>
    /// Runs the exit (see <see cref="ExitAsync"/>: the Reminder activations, then the exit parts, which save the
    /// pending edit), then applies the downloaded update, which ends the process and starts the new version. The
    /// exit waits up to <see cref="UpdateScheduler.StopTimeout"/> for a running update check, or until its parts end
    /// if they take longer; when one still runs, it skips the apply and starts the current version again, and the
    /// update applies at the next normal start (<see cref="UpdateScheduler.RestartAfterExit"/>). Once an exit has
    /// started it only returns that exit. When applying fails, the app shuts down as usual.
    /// </summary>
    public Task RestartToUpdateAsync()
    {
        if (_updates is null)
        {
            return Task.CompletedTask;
        }

        if (_exit is null)
        {
            _restartToUpdate = true;
        }

        return ExitAsync();
    }

    /// <summary>Starts the exit without waiting for it; see <see cref="ExitAsync"/>.</summary>
    public void Exit() => _ = ExitAsync();

    /// <summary>
    /// Stops the update checks (a running download is cancelled, not waited for), waits for the Reminder activations
    /// still running (see <see cref="RunActivationAsync"/>), then runs the exit parts handed to
    /// <see cref="LaunchAsync"/>, waiting for each: prepare the window, stop the host, stop the system hooks, release
    /// the hotkey, release the tray and the single-instance guard (this shell), dispose the host. Then comes Restart
    /// to update, if one was asked for, and the shutdown. A failing part is logged and the exit carries on. Calling
    /// it again returns the exit already under way.
    /// </summary>
    public Task ExitAsync() => _exit ??= RunExitAsync();

    // Stops the update checks. Only Restart to update waits (up to UpdateScheduler.StopTimeout) for a running check,
    // so the apply never races a download still writing to, or cleaning, the packages folder; true when none runs.
    // A plain exit or a session end only cancels it.
    private async Task<bool> StopUpdateChecksAsync()
    {
        if (_updates is null)
        {
            return true;
        }

        if (_restartToUpdate)
        {
            return await _updates.StopAsync();
        }

        _updates.Dispose();
        return true;
    }

    private async Task RunExitAsync()
    {
        _exiting = true;
        // Started first, so its wait overlaps the activations' (see StopUpdateChecksAsync).
        var checksEnded = StopUpdateChecksAsync();
        try
        {
            await _exitStarted.CancelAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Cancelling the launch activation failed; exiting anyway");
        }

        // The owner's action from a toast (the one that launched the app, or one clicked as the exit started) lands
        // before the flush and the host stop.
        await WaitForActivationsAsync();

        if (_exitParts is { } parts)
        {
            await RunPartAsync("prepare the window", parts.PrepareWindow);
            await RunPartAsync("stop the host", parts.StopHost);
            await RunPartAsync("stop the system hooks", parts.StopSystemHooks);
            await RunPartAsync("release the hotkey", parts.ReleaseHotkey);
        }

        // Releases the single-instance guard, so the restarted process becomes the running copy.
        Dispose();
        if (_exitParts is { } last)
        {
            await RunPartAsync("dispose the host", last.DisposeHost);
        }

        if (_restartToUpdate && _updates is not null)
        {
            _updates.RestartAfterExit(await checksEnded);
        }

        lifetime.Shutdown();
    }

    private async Task RunPartAsync(string name, Func<Task> part)
    {
        try
        {
            await part();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "The exit part \"{Part}\" failed; exiting anyway", name);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        tray.LeftClicked -= OnLeftClicked;
        tray.OpenRequested -= window.ShowAndFocus;
        tray.QuickAddRequested -= OnQuickAddRequested;
        tray.SettingsRequested -= OnSettingsRequested;
        tray.ExitRequested -= Exit;
        tray.RestartToUpdateRequested -= RestartToUpdate;
        if (_updates is not null)
        {
            _updates.PendingVersionChanged -= OnPendingVersionChanged;
        }

        tray.Dispose();
        singleInstance.Dispose();
    }

    // A later launch: show the window unless it asked to stay minimized (a second auto-start, say).
    private void OnSecondLaunch(IReadOnlyList<string> arguments)
    {
        if (_exiting)
        {
            return;
        }

        var launch = SecondLaunchArguments.Parse(arguments);
        if (!launch.Minimized)
        {
            window.ShowAndFocus();
        }
    }

    private void OnLeftClicked()
    {
        if (window.IsVisible)
        {
            window.Hide();
        }
        else
        {
            window.ShowAndFocus();
        }
    }

    private void OnPendingVersionChanged() => tray.ShowRestartToUpdate(_updates?.PendingVersion is not null);

    private void OnQuickAddRequested() => QuickAddRequested?.Invoke();

    private void OnSettingsRequested() => SettingsRequested?.Invoke();
}
