using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.App.Shell;

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
    public void The_menu_reads_Open_Quick_add_Settings_then_Exit_after_a_separator()
    {
        var menu = TrayMenu.Build(() => { }, () => { }, () => { }, () => { });

        CollectionAssert.AreEqual(
            new[] { "Open TaskDesk", "Quick add", "Settings", "-", "Exit" },
            menu.Items.Select(i => i is MenuFlyoutItem item ? item.Text : i is MenuFlyoutSeparator ? "-" : "?").ToArray());
    }
}
