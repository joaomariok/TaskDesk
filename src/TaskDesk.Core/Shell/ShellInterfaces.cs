namespace TaskDesk.Core.Shell;

/// <summary>The notification-area icon and its context menu.</summary>
public interface ITrayIcon : IDisposable
{
    event Action? LeftClicked;
    event Action? OpenRequested;
    event Action? SettingsRequested;
    event Action? ExitRequested;

    void Show();

    /// <summary>Shows a transient balloon/notification next to the icon.</summary>
    void ShowHint(string message);
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

    /// <summary>Asks the first instance to show and focus its window.</summary>
    Task SignalExistingAsync(CancellationToken cancellationToken);

    /// <summary>Starts listening for later launches; the callback may run on any thread.</summary>
    void Listen(Action onSecondLaunch);
}

public interface IAppShutdown
{
    void Shutdown();
}
