using Microsoft.UI.Xaml.Controls;

namespace TaskDesk.App.Shell;

/// <summary>
/// The tray icon's menu: Open TaskDesk, Quick add, Settings, then Restart to update (only while an update is
/// pending) and Exit. H.NotifyIcon.WinUI shows it as a native popup
/// menu (its PopupMenu mode), which copies each item's text and, on a click, runs the item's <c>Command</c> with its
/// <c>CommandParameter</c>; it never raises the item's <c>Click</c> event. So each item does its work through its
/// Command.
/// </summary>
internal static class TrayMenu
{
    public static MenuFlyout Build(
        Action open, Action quickAdd, Action settings, Action exit, Action? restartToUpdate = null)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Open TaskDesk", open));
        menu.Items.Add(Item("Quick add", quickAdd));
        menu.Items.Add(Item("Settings", settings));
        menu.Items.Add(new MenuFlyoutSeparator());
        if (restartToUpdate is not null)
        {
            menu.Items.Add(Item("Restart to update", restartToUpdate));
        }

        menu.Items.Add(Item("Exit", exit));
        return menu;
    }

    private static MenuFlyoutItem Item(string text, Action action) =>
        new() { Text = text, Command = new ActionCommand(action) };
}
