using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Shell;

/// <summary>Tray icon built on H.NotifyIcon.Wpf. Covered by manual checks only.</summary>
internal sealed class HNotifyTrayIcon : ITrayIcon
{
    private readonly TaskbarIcon _icon;

    // True while the last balloon shown is a Reminder (a click on a hint balloon opens nothing).
    private bool _reminderBalloon;

    public HNotifyTrayIcon()
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/TaskDesk.ico")).Stream;
        _icon = new TaskbarIcon
        {
            ToolTipText = "TaskDesk",
            Icon = new System.Drawing.Icon(stream),
            ContextMenu = BuildMenu(),
            MenuActivation = PopupActivationMode.RightClick,
            NoLeftClickDelay = true,
        };
        _icon.TrayLeftMouseUp += (_, _) => LeftClicked?.Invoke();
        _icon.TrayBalloonTipClicked += (_, _) =>
        {
            if (_reminderBalloon)
            {
                BalloonClicked?.Invoke();
            }
        };
    }

    public event Action? LeftClicked;
    public event Action? OpenRequested;
    public event Action? QuickAddRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public void Show() => _icon.ForceCreate();

    /// <summary>Raised when the owner clicks a balloon shown by <see cref="ShowBalloon"/>.</summary>
    public event Action? BalloonClicked;

    public void ShowHint(string message) => _icon.Dispatcher.Invoke(() =>
    {
        _reminderBalloon = false;
        _icon.ShowNotification("TaskDesk", message);
    });

    /// <summary>Shows a Reminder balloon (the notification fallback); safe to call from any thread.</summary>
    public void ShowBalloon(string title, string message) => _icon.Dispatcher.Invoke(() =>
    {
        _reminderBalloon = true;
        _icon.ShowNotification(title, message);
    });

    public void Dispose() => _icon.Dispose();

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Open TaskDesk", () => OpenRequested?.Invoke()));
        menu.Items.Add(Item("Quick add", () => QuickAddRequested?.Invoke()));
        menu.Items.Add(Item("Settings", () => SettingsRequested?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Exit", () => ExitRequested?.Invoke()));
        return menu;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
