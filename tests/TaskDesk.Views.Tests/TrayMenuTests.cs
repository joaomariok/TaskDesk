using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.App.Shell;
using TaskDesk.Core.Shell;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The tray menu (shell source linked in). H.NotifyIcon.WinUI's PopupMenu mode turns each MenuFlyoutItem into a
/// native menu item that, when clicked, runs <c>item.Command?.TryExecute(item.CommandParameter)</c> and nothing
/// else: an item wired only to Click does nothing. The test does what the popup menu does.
/// </summary>
[TestClass]
public sealed class TrayMenuTests
{
    [UITestMethod]
    [DataRow("Open TaskDesk")]
    [DataRow("Quick add")]
    [DataRow("Settings")]
    [DataRow("Exit")]
    public void Clicking_an_item_in_the_native_popup_menu_runs_that_item_only(string text)
    {
        var ran = new List<string>();
        var menu = TrayMenu.Build(
            () => ran.Add("Open TaskDesk"), () => ran.Add("Quick add"), () => ran.Add("Settings"), () => ran.Add("Exit"));
        var item = menu.Items.OfType<MenuFlyoutItem>().Single(i => i.Text == text);

        Assert.IsNotNull(item.Command, $"'{text}' has no command, so the tray's popup menu cannot run it.");
        Assert.IsTrue(item.Command.CanExecute(item.CommandParameter));
        item.Command.Execute(item.CommandParameter);

        CollectionAssert.AreEqual(new[] { text }, ran);
    }

    [UITestMethod]
    [DataRow("Open TaskDesk")]
    [DataRow("Quick add")]
    [DataRow("Settings")]
    [DataRow("Restart to update")]
    [DataRow("Exit")]
    public void With_an_update_pending_clicking_an_item_in_the_native_popup_menu_runs_that_item_only(string text)
    {
        var ran = new List<string>();
        var menu = TrayMenu.Build(
            () => ran.Add("Open TaskDesk"), () => ran.Add("Quick add"), () => ran.Add("Settings"), () => ran.Add("Exit"),
            restartToUpdate: () => ran.Add("Restart to update"));
        var item = menu.Items.OfType<MenuFlyoutItem>().Single(i => i.Text == text);

        Assert.IsNotNull(item.Command, $"'{text}' has no command, so the tray's popup menu cannot run it.");
        Assert.IsTrue(item.Command.CanExecute(item.CommandParameter));
        item.Command.Execute(item.CommandParameter);

        CollectionAssert.AreEqual(new[] { text }, ran);
    }

    [UITestMethod]
    public void With_an_update_pending_Restart_to_update_sits_above_Exit()
    {
        var menu = TrayMenu.Build(() => { }, () => { }, () => { }, () => { }, restartToUpdate: () => { });

        CollectionAssert.AreEqual(
            new[] { "Open TaskDesk", "Quick add", "Settings", "-", "Restart to update", "Exit" },
            menu.Items.Select(i => i is MenuFlyoutItem item ? item.Text : i is MenuFlyoutSeparator ? "-" : "?").ToArray());
    }

    [UITestMethod]
    public void Without_an_update_pending_the_menu_has_no_Restart_to_update()
    {
        var menu = TrayMenu.Build(() => { }, () => { }, () => { }, () => { }, restartToUpdate: null);

        Assert.IsFalse(menu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "Restart to update"));
    }

    [UITestMethod]
    public void The_menu_reads_Open_Quick_add_Settings_then_Exit_after_a_separator()
    {
        var menu = TrayMenu.Build(() => { }, () => { }, () => { }, () => { });

        CollectionAssert.AreEqual(
            new[] { "Open TaskDesk", "Quick add", "Settings", "-", "Exit" },
            menu.Items.Select(i => i is MenuFlyoutItem item ? item.Text : i is MenuFlyoutSeparator ? "-" : "?").ToArray());
    }

    // The native popup menu is drawn by Windows, not XAML: the process's preferred app mode themes it.
    // uxtheme's PreferredAppMode: 1 AllowDark (follows the Windows app mode), 2 ForceDark, 3 ForceLight.
    [TestMethod]
    [DataRow(ThemeChoice.Dark, 2)]
    [DataRow(ThemeChoice.Light, 3)]
    [DataRow(ThemeChoice.System, 1)]
    public void The_native_menu_follows_the_theme_setting(ThemeChoice theme, int preferredAppMode) =>
        Assert.AreEqual(preferredAppMode, (int)NativeMenuTheme.ModeFor(theme));
}
