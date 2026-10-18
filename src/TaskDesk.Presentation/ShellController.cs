using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation;

/// <summary>Ties the tray icon, main window, single-instance guard and auto-start together.</summary>
public sealed class ShellController(
    ITrayIcon tray,
    IMainWindow window,
    ISingleInstance singleInstance,
    IShellSettingsStore settingsStore,
    AutoStartService autoStart,
    IAppShutdown lifetime,
    ILogger<ShellController>? logger = null) : IDisposable
{
    public const string TrayHint = "TaskDesk is still running in the tray";

    private readonly ILogger _logger = logger ?? NullLogger<ShellController>.Instance;
    private readonly List<Func<Task>> _exitSteps = [];

    // Never disposed: a Reminder activation may still read its token after the exit, and it holds no timer.
    private readonly CancellationTokenSource _exitStarted = new();

    // Reminder activations still running, which the exit waits for; one can arrive on any thread.
    private readonly Lock _activationsGate = new();
    private readonly List<Task> _activations = [];
    private bool _activationsClosed;
    private Task? _exit;
    private bool _exiting;
    private bool _disposed;

    /// <summary>Raised by the tray menu's "Quick add"; the App opens the quick-add window.</summary>
    public event Action? QuickAddRequested;

    /// <summary>Raised by the tray menu; the App opens the Settings window in response.</summary>
    public event Action? SettingsRequested;

    /// <summary>
    /// Raised when a later launch was started by a click on a Reminder notification; the App hands it to
    /// <see cref="ReminderActivationHandler"/>. May be raised on any thread.
    /// </summary>
    public event Action<NotificationActivation>? ActivationRequested;

    /// <summary>
    /// Returns false when another instance already runs (it has been signalled and this
    /// process should exit); otherwise sets everything up.
    /// </summary>
    public async Task<bool> StartAsync(bool minimized, string exePath, CancellationToken cancellationToken = default)
    {
        if (!singleInstance.TryAcquire())
        {
            await singleInstance.SignalExistingAsync(SecondLaunchArguments.Build(minimized, null), cancellationToken);
            return false;
        }

        singleInstance.Listen(OnSecondLaunch);
        autoStart.Apply(settingsStore.Load().StartWithWindows, exePath);

        tray.LeftClicked += OnLeftClicked;
        tray.OpenRequested += window.ShowAndFocus;
        tray.QuickAddRequested += OnQuickAddRequested;
        tray.SettingsRequested += OnSettingsRequested;
        tray.ExitRequested += Exit;
        tray.Show();

        if (!minimized)
        {
            window.ShowAndFocus();
        }

        return true;
    }

    /// <summary>
    /// Cancelled when the exit starts. A Reminder activation reads it to skip what only a running app needs, such
    /// as showing the window; the action the owner chose (Mark done, Snooze) still completes.
    /// </summary>
    public CancellationToken ExitStarted => _exitStarted.Token;

    /// <summary>
    /// The end of start-up: <see cref="StartAsync"/>, then the Reminder activation that launched the app, then
    /// <paramref name="running"/> (work that runs until the exit, such as the Reminder checks). From
    /// <see cref="StartAsync"/> on, the tray's Exit and session end can start the exit, which disposes what the
    /// later steps use. The activation gets <see cref="ExitStarted"/>, and the exit waits for it before its steps,
    /// so a Mark done or Snooze that launched the app is kept. A failing activation is logged and start-up carries
    /// on. Nothing runs after an exit has started. Returns false when another instance already runs (see
    /// <see cref="StartAsync"/>).
    /// </summary>
    public async Task<bool> LaunchAsync(
        bool minimized,
        string exePath,
        Func<CancellationToken, Task>? launchActivation,
        Action running,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(running);
        if (!await StartAsync(minimized, exePath, cancellationToken))
        {
            return false;
        }

        if (launchActivation is not null && !_exiting)
        {
            await RunActivationAsync(launchActivation);
        }

        if (!_exiting)
        {
            running();
        }

        return true;
    }

    /// <summary>
    /// Runs a Reminder activation (the toast that launched the app, or a click on a toast or balloon in the running
    /// app) with <see cref="ExitStarted"/>. The exit waits for every activation still running before its steps, so
    /// a Mark done or Snooze clicked as the exit starts is kept. One that arrives once the exit steps have started
    /// is dropped: they stop and dispose what it would write to. Callable from any thread; never throws.
    /// </summary>
    public Task RunActivationAsync(Func<CancellationToken, Task> activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        lock (_activationsGate)
        {
            if (_activationsClosed)
            {
                _logger.LogInformation("A Reminder activation arrived during the exit and was dropped");
                return Task.CompletedTask;
            }

            _activations.RemoveAll(t => t.IsCompleted);
            var running = RunGuardedAsync(activation);
            _activations.Add(running);
            return running;
        }
    }

    // Never throws: the exit waits for it.
    private async Task RunGuardedAsync(Func<CancellationToken, Task> activation)
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

    /// <summary>
    /// Adds work that must finish before the app shuts down (for example flushing a pending save, then
    /// stopping the host). Steps run in the order they were added. Every step must be added before anything
    /// can start the exit: a step added once the exit has started is rejected, never silently skipped.
    /// </summary>
    public void AddExitStep(Func<Task> step)
    {
        if (_exiting)
        {
            throw new InvalidOperationException("The exit has already started; add exit steps before it can start.");
        }

        _exitSteps.Add(step);
    }

    /// <summary>Starts the exit without waiting for it; see <see cref="ExitAsync"/>.</summary>
    public void Exit() => _ = ExitAsync();

    /// <summary>
    /// Waits for the Reminder activations still running (see <see cref="RunActivationAsync"/>), then runs every exit
    /// step in order, waiting for each, then disposes the tray and the single-instance guard and shuts the app down. A failing step is logged and the exit carries on. Calling it again
    /// returns the exit already under way.
    /// </summary>
    public Task ExitAsync() => _exit ??= RunExitAsync();

    private async Task RunExitAsync()
    {
        _exiting = true;
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

        foreach (var step in _exitSteps)
        {
            try
            {
                await step();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "An exit step failed; exiting anyway");
            }
        }

        Dispose();
        lifetime.Shutdown();
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
        tray.Dispose();
        singleInstance.Dispose();
    }

    // A later launch: act on its Reminder notification click, or show the window unless it asked to stay
    // minimized (a second auto-start, say).
    private void OnSecondLaunch(IReadOnlyList<string> arguments)
    {
        if (_exiting)
        {
            return;
        }

        var launch = SecondLaunchArguments.Parse(arguments);
        if (launch.Activation is { } activation)
        {
            ActivationRequested?.Invoke(activation);
        }
        else if (!launch.Minimized)
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

    private void OnQuickAddRequested() => QuickAddRequested?.Invoke();

    private void OnSettingsRequested() => SettingsRequested?.Invoke();
}
