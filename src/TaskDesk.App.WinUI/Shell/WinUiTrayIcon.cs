using H.NotifyIcon;
using H.NotifyIcon.Core;
using TrayMessageWindow = H.NotifyIcon.Core.MessageWindow;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Shell;

/// <summary>
/// Tray icon built on H.NotifyIcon.WinUI: left click, and a menu with Open TaskDesk, Quick add, Settings and Exit,
/// and the Reminder balloons that stand in for toasts in sandbox mode. Create it on the UI thread. Covered by
/// manual checks only.
/// </summary>
internal sealed class WinUiTrayIcon : ITrayIcon, IBalloonTray
{
    private readonly TaskbarIcon _icon;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    // True while the last balloon shown is a Reminder (a click on a hint balloon opens nothing).
    private bool _reminderBalloon;
    private bool _listening;

    public WinUiTrayIcon()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Open TaskDesk", () => OpenRequested?.Invoke()));
        menu.Items.Add(Item("Quick add", () => QuickAddRequested?.Invoke()));
        menu.Items.Add(Item("Settings", () => SettingsRequested?.Invoke()));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Exit", () => ExitRequested?.Invoke()));
        _icon = new TaskbarIcon
        {
            ToolTipText = "TaskDesk",
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "TaskDesk.ico")),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            LeftClickCommand = new ActionCommand(() => LeftClicked?.Invoke()),
            NoLeftClickDelay = true,
        };
    }

    public event Action? LeftClicked;
    public event Action? OpenRequested;
    public event Action? QuickAddRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    /// <summary>Raised when the owner clicks a balloon shown by <see cref="ShowBalloon"/>.</summary>
    public event Action? BalloonClicked;

    public void Show()
    {
        _icon.ForceCreate(false);
        if (!_listening)
        {
            // H.NotifyIcon.WinUI leaves its TrayBalloonTipClicked event out by default; the icon's own message
            // window reports the click (NIN_BALLOONUSERCLICK) as a mouse event.
            _listening = true;
            _icon.TrayIcon.MessageWindow.MouseEventReceived += OnMouseEvent;
        }
    }

    public void ShowHint(string message) => _dispatcher.TryEnqueue(() =>
    {
        _reminderBalloon = false;
        _icon.ShowNotification("TaskDesk", message);
    });

    /// <summary>Shows a Reminder balloon (the notification fallback); safe to call from any thread.</summary>
    public void ShowBalloon(string title, string message) => _dispatcher.TryEnqueue(() =>
    {
        _reminderBalloon = true;
        _icon.ShowNotification(title, message);
    });

    public void Dispose()
    {
        if (_listening)
        {
            _listening = false;
            _icon.TrayIcon.MessageWindow.MouseEventReceived -= OnMouseEvent;
        }

        _icon.Dispose();
    }

    private void OnMouseEvent(object? sender, TrayMessageWindow.MouseEventReceivedEventArgs e)
    {
        if (e.MouseEvent == MouseEvent.BalloonToolTipClicked)
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (_reminderBalloon)
                {
                    BalloonClicked?.Invoke();
                }
            });
        }
    }

    private static MenuFlyoutItem Item(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }
}
