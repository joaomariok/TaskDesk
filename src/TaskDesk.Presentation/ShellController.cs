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
    /// Runs every exit step in order, waiting for each, then disposes the tray and the single-instance
    /// guard and shuts the app down. A failing step is logged and the exit carries on. Calling it again
    /// returns the exit already under way.
    /// </summary>
    public Task ExitAsync() => _exit ??= RunExitAsync();

    private async Task RunExitAsync()
    {
        _exiting = true;
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
