using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Views.Dialogs;

namespace TaskDesk.Views.Tests;

/// <summary>
/// Dialogs and file pickers open on the window the owner is using: the last activated of the app's open windows,
/// so a dialog from Settings opens on Settings and one from the main window on the main window.
/// </summary>
[TestClass]
public sealed class ActiveWindowTests
{
    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task The_last_activated_open_window_is_current_and_a_closed_one_hands_back_to_the_other()
    {
        var ct = TestContext.CancellationToken;
        var active = new ActiveWindow();
        var main = NewWindow("Main");
        var settings = NewWindow("Settings");
        try
        {
            active.Register(main);
            main.Activate();
            await UiWait.UntilAsync(() => active.Current == main, () => "The main window is not current.", ct);

            active.Register(settings);
            settings.Activate();
            await UiWait.UntilAsync(() => active.Current == settings, () => "Settings is not current once activated.", ct);

            settings.Close();
            Assert.AreSame(main, active.Current, "Closing Settings hands the dialogs back to the main window.");
        }
        finally
        {
            main.Close();
        }

        Assert.IsNull(active.Current, "No window is open.");
    }

    [UITestMethod]
    public void A_registered_window_is_current_before_any_activation()
    {
        var active = new ActiveWindow();
        var main = NewWindow("Main");
        try
        {
            active.Register(main);

            Assert.AreSame(main, active.Current);
        }
        finally
        {
            main.Close();
        }
    }

    [UITestMethod]
    public async Task A_dialog_opens_on_the_active_window()
    {
        var ct = TestContext.CancellationToken;
        var active = new ActiveWindow();
        var main = NewWindow("Main");
        var settings = NewWindow("Settings");
        try
        {
            active.Register(main);
            main.Activate();
            active.Register(settings);
            settings.Activate();
            await UiWait.UntilAsync(
                () => active.Current == settings && settings.Content.XamlRoot is not null,
                () => "Settings never became current.", ct);
            var dialogs = new DialogService(() => active.Current);

            var shown = dialogs.ShowMessageAsync("Export", "Exported.");
            var dialog = await DialogHost.OpenDialogAsync(settings.Content.XamlRoot, ct);
            Assert.AreEqual("Export", dialog.Title);
            Assert.IsNull(main.Content.XamlRoot is { } root ? DialogHost.OpenDialog(root) : null, "The main window shows no dialog.");
            DialogHost.Press(dialog, "CloseButton");
            await shown;
        }
        finally
        {
            settings.Close();
            main.Close();
        }
    }

    private static Window NewWindow(string title) => new() { Title = title, Content = new Grid() };
}
