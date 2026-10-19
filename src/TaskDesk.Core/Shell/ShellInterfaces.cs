namespace TaskDesk.Core.Shell;

/// <summary>The notification-area icon and its context menu.</summary>
public interface ITrayIcon : IDisposable
{
    event Action? LeftClicked;
    event Action? OpenRequested;
    event Action? QuickAddRequested;
    event Action? SettingsRequested;
    event Action? ExitRequested;

    /// <summary>Raised by the menu's "Restart to update", which it shows only while an update is pending.</summary>
    event Action? RestartToUpdateRequested;

    void Show();

    /// <summary>Shows a transient balloon/notification next to the icon.</summary>
    void ShowHint(string message);
}

/// <summary>
/// The tray icon's Reminder balloons, the fallback when rich notifications are unavailable (SPEC 7.3).
/// </summary>
public interface IBalloonTray
{
    /// <summary>Raised when the owner clicks a balloon shown by <see cref="ShowBalloon"/> (not a hint balloon).</summary>
    event Action? BalloonClicked;

    /// <summary>Shows a Reminder balloon; safe to call from any thread.</summary>
    void ShowBalloon(string title, string message);
}

/// <summary>The main window, as far as the shell cares. Implementations marshal to the UI thread.</summary>
public interface IMainWindow
{
    bool IsVisible { get; }

    /// <summary>Shows the window (restoring it if minimized) and gives it focus.</summary>
    void ShowAndFocus();

    void Hide();
}

/// <summary>Guarantees one running process; later launches wake the first one.</summary>
public interface ISingleInstance : IDisposable
{
    /// <summary>True when this process is the first instance.</summary>
    bool TryAcquire();

    /// <summary>
    /// Wakes the first instance and forwards this launch's arguments to it (see
    /// <see cref="SecondLaunchArguments"/>).
    /// </summary>
    Task SignalExistingAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    /// <summary>
    /// Starts listening for later launches; the callback gets the arguments they forwarded and may run on
    /// any thread.
    /// </summary>
    void Listen(Action<IReadOnlyList<string>> onSecondLaunch);
}

public interface IAppShutdown
{
    void Shutdown();
}
