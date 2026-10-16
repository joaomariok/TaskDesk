using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation;

/// <summary>Ties the tray icon, main window, single-instance guard and auto-start together.</summary>
public sealed class ShellController(
    ITrayIcon tray,
    IMainWindow window,
    ISingleInstance singleInstance,
    IShellSettingsStore settingsStore,
    AutoStartService autoStart,
    IAppShutdown lifetime) : IDisposable
{
    public const string TrayHint = "TaskDesk is still running in the tray";

    private bool _exiting;
    private bool _disposed;

    /// <summary>Raised by the tray menu; the App opens the Settings window in response.</summary>
    public event Action? SettingsRequested;

    /// <summary>
    /// Returns false when another instance already runs (it has been signalled and this
    /// process should exit); otherwise sets everything up.
    /// </summary>
    public async Task<bool> StartAsync(bool minimized, string exePath, CancellationToken cancellationToken = default)
    {
        if (!singleInstance.TryAcquire())
        {
            await singleInstance.SignalExistingAsync(cancellationToken);
            return false;
        }

        singleInstance.Listen(window.ShowAndFocus);
        autoStart.Apply(settingsStore.Load().StartWithWindows, exePath);

        tray.LeftClicked += OnLeftClicked;
        tray.OpenRequested += window.ShowAndFocus;
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

    public void Exit()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
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
        tray.SettingsRequested -= OnSettingsRequested;
        tray.ExitRequested -= Exit;
        tray.Dispose();
        singleInstance.Dispose();
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

    private void OnSettingsRequested() => SettingsRequested?.Invoke();
}
