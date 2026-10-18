using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Views.Dialogs;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The one dialog service (spec #52, #56): a native content dialog on the active window, with the shared Fluent
/// dialog's rules. Re-expresses the WPF FluentDialog load test and the dialog behaviour of the FlaUI
/// NavigationTests delete-list test in-process.
/// </summary>
[TestClass]
public sealed class DialogServiceTests
{
    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task A_confirmation_shows_its_title_body_and_buttons_and_returns_the_primary_choice()
    {
        var ct = TestContext.CancellationToken;
        using var host = await DialogHost.OpenAsync(ct);
        var service = new DialogService(() => host.Window);

        var shown = service.ShowAsync(new DialogRequest("Archive list", "Archive the list?", ["Archive"]));
        var dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);

        Assert.AreEqual("Archive list", dialog.Title);
        CollectionAssert.Contains(DialogHost.Texts(dialog).ToList(), "Archive the list?");
        CollectionAssert.AreEqual(
            new[] { "Archive", "Cancel" }, DialogHost.Buttons(dialog).Select(b => b.Content).ToList());
        Assert.AreEqual(ContentDialogButton.Primary, dialog.DefaultButton);
        await UiWait.UntilAsync(() => AutomationProperties.GetAutomationId(dialog) == "FluentDialog",
            () => "The open dialog has no FluentDialog AutomationId.", ct);
        Assert.AreEqual("Archive list", AutomationProperties.GetName(dialog));

        DialogHost.Press(dialog, "PrimaryButton");
        var result = await shown;

        Assert.AreEqual(0, result.ChoiceIndex);
        Assert.IsFalse(result.IsCancelled);
    }

    [UITestMethod]
    public async Task A_destructive_confirmation_makes_Cancel_the_default_and_the_focus_and_Cancel_returns_the_cancel_index()
    {
        var ct = TestContext.CancellationToken;
        using var host = await DialogHost.OpenAsync(ct);
        var service = new DialogService(() => host.Window);

        var shown = service.ShowAsync(
            new DialogRequest("Delete list", "Delete the list?", ["Delete"]) { IsDestructive = true });
        var dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);

        // Enter answers Cancel, so Enter never destroys data.
        Assert.AreEqual(ContentDialogButton.Close, dialog.DefaultButton);
        var cancel = VisualTree.Find<Button>(dialog, "CloseButton");
        await UiWait.UntilAsync(
            () => ReferenceEquals(FocusManager.GetFocusedElement(dialog.XamlRoot), cancel),
            () => $"Cancel did not take the focus; {FocusManager.GetFocusedElement(dialog.XamlRoot)} has it.", ct);
        Assert.AreEqual("FluentDialog", AutomationProperties.GetAutomationId(dialog));

        DialogHost.Press(dialog, "CloseButton");
        var result = await shown;

        Assert.IsTrue(result.IsCancelled);
        Assert.AreEqual(DialogService.CancelIndex, result.ChoiceIndex);
    }

    [UITestMethod]
    public async Task UI_Automation_finds_the_dialog_only_once_Cancel_holds_the_focus_so_a_key_pressed_then_reaches_it()
    {
        var ct = TestContext.CancellationToken;
        using var host = await DialogHost.OpenAsync(ct);
        var service = new DialogService(() => host.Window);
        var root = host.Window.Content.XamlRoot;
        // Each layout pass of the opening dialog: whenever it carries its AutomationId, Cancel has the focus.
        // (Recorded, not asserted: an exception in a layout handler would end the test host.)
        string? violation = null;
        void Check(object? sender, object e)
        {
            if (DialogHost.OpenDialog(root) is { } open && AutomationProperties.GetAutomationId(open) == "FluentDialog"
                && FocusManager.GetFocusedElement(root) is not Button { Name: "CloseButton" })
            {
                violation ??= $"The dialog had its AutomationId while {FocusManager.GetFocusedElement(root)} had the focus.";
            }
        }

        ((Microsoft.UI.Xaml.FrameworkElement)host.Window.Content).LayoutUpdated += Check;
        try
        {
            var shown = service.ShowAsync(
                new DialogRequest("Delete list", "Delete the list?", ["Delete"]) { IsDestructive = true });
            var dialog = await DialogHost.OpenDialogAsync(root, ct);
            await UiWait.UntilAsync(() => AutomationProperties.GetAutomationId(dialog) == "FluentDialog",
                () => "The open dialog has no FluentDialog AutomationId.", ct);
            Assert.AreSame(VisualTree.Find<Button>(dialog, "CloseButton"), FocusManager.GetFocusedElement(root));
            Assert.IsNull(violation, violation);

            DialogHost.Press(dialog, "CloseButton");
            await shown;
        }
        finally
        {
            ((Microsoft.UI.Xaml.FrameworkElement)host.Window.Content).LayoutUpdated -= Check;
        }
    }

    [UITestMethod]
    public async Task A_second_choice_is_the_secondary_button_and_returns_its_index()
    {
        var ct = TestContext.CancellationToken;
        using var host = await DialogHost.OpenAsync(ct);
        var service = new DialogService(() => host.Window);

        var shown = service.ShowAsync(new DialogRequest("Import", "How?", ["Merge", "Replace everything"]));
        var dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);

        CollectionAssert.AreEqual(
            new[] { "Merge", "Replace everything", "Cancel" }, DialogHost.Buttons(dialog).Select(b => b.Content).ToList());
        Assert.AreEqual(ContentDialogButton.Primary, dialog.DefaultButton);

        DialogHost.Press(dialog, "SecondaryButton");
        var result = await shown;

        Assert.AreEqual(1, result.ChoiceIndex);
    }

    [UITestMethod]
    public async Task An_input_box_starts_focused_with_its_text_selected_and_returns_the_typed_text()
    {
        var ct = TestContext.CancellationToken;
        using var host = await DialogHost.OpenAsync(ct);
        var service = new DialogService(() => host.Window);

        var shown = service.ShowAsync(new DialogRequest("Rename list", "Choose a new name.", ["Rename"])
        {
            Input = new DialogInput("List name", "Inbox", MaxLength: 30),
        });
        var dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);

        var box = VisualTree.Descendants(dialog).OfType<TextBox>().Single();
        Assert.AreEqual("List name", box.Header);
        Assert.AreEqual("List name", AutomationProperties.GetName(box));
        Assert.AreEqual("Inbox", box.Text);
        Assert.AreEqual(30, box.MaxLength);
        await UiWait.UntilAsync(
            () => ReferenceEquals(FocusManager.GetFocusedElement(dialog.XamlRoot), box) && box.SelectedText == "Inbox",
            () => $"The box was not focused with its text selected (selected '{box.SelectedText}').", ct);

        box.Text = "Groceries";
        DialogHost.Press(dialog, "PrimaryButton");
        var result = await shown;

        Assert.AreEqual(0, result.ChoiceIndex);
        Assert.AreEqual("Groceries", result.Text);
    }

    [UITestMethod]
    public async Task A_message_has_only_an_OK_button_which_is_the_default()
    {
        var ct = TestContext.CancellationToken;
        using var host = await DialogHost.OpenAsync(ct);
        var service = new DialogService(() => host.Window);

        var shown = service.ShowMessageAsync("TaskDesk", "Something to read.");
        var dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);

        Assert.AreEqual("TaskDesk", dialog.Title);
        CollectionAssert.Contains(DialogHost.Texts(dialog).ToList(), "Something to read.");
        CollectionAssert.AreEqual(new[] { "OK" }, DialogHost.Buttons(dialog).Select(b => b.Content).ToList());
        Assert.AreEqual(ContentDialogButton.Close, dialog.DefaultButton);

        DialogHost.Press(dialog, "CloseButton");
        await shown;
    }

    [UITestMethod]
    [DataRow(ElementTheme.Light)]
    [DataRow(ElementTheme.Dark)]
    public async Task A_dialog_takes_the_theme_of_the_window_it_opens_on(ElementTheme theme)
    {
        var ct = TestContext.CancellationToken;
        using var host = await DialogHost.OpenAsync(ct);
        // The theme service sets the chosen theme on the window's content root, not on the application.
        ((FrameworkElement)host.Window.Content).RequestedTheme = theme;
        var service = new DialogService(() => host.Window);

        var shown = service.ShowMessageAsync("TaskDesk", "Something to read.");
        var dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);
        var actual = dialog.ActualTheme;
        DialogHost.Press(dialog, "CloseButton");
        await shown;

        Assert.AreEqual(theme, actual);
    }

    [UITestMethod]
    public async Task Dialogs_opened_one_after_another_stay_on_the_window_the_first_opened_on()
    {
        var ct = TestContext.CancellationToken;
        using var settings = await DialogHost.OpenAsync(ct);
        using var main = await DialogHost.OpenAsync(ct);
        var active = settings.Window;
        var service = new DialogService(() => active);
        try
        {
            // An import: the mode choice, then the Replace confirmation straight after it.
            async Task<bool> Import()
            {
                var mode = await service.ShowAsync(new DialogRequest("Import", "How?", ["Merge", "Replace"]));
                return mode.ChoiceIndex == 1
                    && await service.ConfirmAsync("Replace all data", "Sure?", "Replace", destructive: true);
            }

            var import = Import();
            var choice = await DialogHost.OpenDialogAsync(settings.Window.Content.XamlRoot, ct);
            active = main.Window; // The owner clicks the main window while the choice is open.
            DialogHost.Press(choice, "SecondaryButton");

            var confirm = await DialogHost.OpenDialogAsync(settings.Window.Content.XamlRoot, ct);
            Assert.IsNull(DialogHost.OpenDialog(main.Window.Content.XamlRoot), "The confirmation jumped windows.");
            DialogHost.Press(confirm, "PrimaryButton");
            Assert.IsTrue(await import);

            // Once the flow has settled, the next dialog opens on the window that is active then.
            await Task.Delay(100, ct);
            var next = service.ShowMessageAsync("TaskDesk", "Imported.");
            var message = await DialogHost.OpenDialogAsync(main.Window.Content.XamlRoot, ct);
            DialogHost.Press(message, "CloseButton");
            await next;
        }
        finally
        {
            DialogHost.OpenDialog(settings.Window.Content.XamlRoot)?.Hide();
            DialogHost.OpenDialog(main.Window.Content.XamlRoot)?.Hide();
        }
    }
}
