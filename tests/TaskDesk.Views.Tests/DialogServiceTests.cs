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
        Assert.AreEqual("FluentDialog", AutomationProperties.GetAutomationId(dialog));
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

        DialogHost.Press(dialog, "CloseButton");
        var result = await shown;

        Assert.IsTrue(result.IsCancelled);
        Assert.AreEqual(DialogService.CancelIndex, result.ChoiceIndex);
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
}
