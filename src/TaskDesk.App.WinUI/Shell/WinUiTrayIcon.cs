using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Shell;

/// <summary>
/// Tray icon built on H.NotifyIcon.WinUI: left click, and a menu with Open TaskDesk, Quick add, Settings and Exit.
/// Create it on the UI thread. Covered by manual checks only.
/// </summary>
internal sealed class WinUiTrayIcon : ITrayIcon
{
    private readonly TaskbarIcon _icon;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

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

    public void Show() => _icon.ForceCreate(false);

    public void ShowHint(string message) =>
        _dispatcher.TryEnqueue(() => _icon.ShowNotification("TaskDesk", message));

    public void Dispose() => _icon.Dispose();

    private static MenuFlyoutItem Item(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }
}
