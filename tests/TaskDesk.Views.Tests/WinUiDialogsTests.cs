using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Import;
using TaskDesk.Presentation;
using TaskDesk.Views.Dialogs;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The Presentation dialog interfaces on the WinUI dialog service: the same titles, texts, choices and defaults
/// as the WPF app's dialogs.
/// </summary>
[TestClass]
public sealed class WinUiDialogsTests
{
    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task Deleting_a_list_asks_with_Cancel_as_the_default_and_Delete_confirms()
    {
        var ct = TestContext.CancellationToken;
        using var host = await DialogHost.OpenAsync(ct);
        IListDialogs dialogs = new WinUiDialogs(new DialogService(() => host.Window));

        var cancelled = dialogs.ConfirmDeleteAsync("Groceries", 3);
        var dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);
        Assert.AreEqual("Delete list", dialog.Title);
        CollectionAssert.Contains(DialogHost.Texts(dialog).ToList(),
            "Delete the list \"Groceries\" and all its tasks (3 open)? This cannot be undone.");
        CollectionAssert.AreEqual(new[] { "Delete", "Cancel" }, DialogHost.Buttons(dialog).Select(b => b.Content).ToList());
        Assert.AreEqual(ContentDialogButton.Close, dialog.DefaultButton);
        DialogHost.Press(dialog, "CloseButton");
        Assert.IsFalse(await cancelled);

        var confirmed = dialogs.ConfirmDeleteAsync("Groceries", 3);
        dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);
        DialogHost.Press(dialog, "PrimaryButton");
        Assert.IsTrue(await confirmed);
    }

    [UITestMethod]
    public async Task Renaming_a_list_asks_for_the_name_and_Cancel_returns_null()
    {
        var ct = TestContext.CancellationToken;
        using var host = await DialogHost.OpenAsync(ct);
        IListDialogs dialogs = new WinUiDialogs(new DialogService(() => host.Window));

        var renamed = dialogs.PromptNameAsync("Inbox");
        var dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);
        Assert.AreEqual("Rename list", dialog.Title);
        CollectionAssert.AreEqual(new[] { "Rename", "Cancel" }, DialogHost.Buttons(dialog).Select(b => b.Content).ToList());
        var box = VisualTree.Descendants(dialog).OfType<TextBox>().Single();
        Assert.AreEqual("List name", box.Header);
        Assert.AreEqual("Inbox", box.Text);
        box.Text = "Home";
        DialogHost.Press(dialog, "PrimaryButton");
        Assert.AreEqual("Home", await renamed);

        var cancelled = dialogs.PromptNameAsync("Inbox");
        dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);
        DialogHost.Press(dialog, "CloseButton");
        Assert.IsNull(await cancelled);
    }

    [UITestMethod]
    public async Task Deleting_an_occurrence_offers_this_occurrence_or_stop_repeating()
    {
        var ct = TestContext.CancellationToken;
        using var host = await DialogHost.OpenAsync(ct);
        IOccurrenceDialogs dialogs = new WinUiDialogs(new DialogService(() => host.Window));

        var skipped = dialogs.AskDeleteOccurrenceAsync("Water plants");
        var dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);
        Assert.AreEqual("Delete repeating task", dialog.Title);
        CollectionAssert.Contains(DialogHost.Texts(dialog).ToList(),
            "\"Water plants\" repeats. Delete only this occurrence (the next one is still created), or stop repeating?");
        CollectionAssert.AreEqual(
            new[] { "Delete this occurrence", "Stop repeating", "Cancel" },
            DialogHost.Buttons(dialog).Select(b => b.Content).ToList());
        DialogHost.Press(dialog, "PrimaryButton");
        Assert.AreEqual(OccurrenceDeleteChoice.DeleteThisOccurrence, await skipped);

        var stopped = dialogs.AskDeleteOccurrenceAsync("Water plants");
        dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);
        DialogHost.Press(dialog, "SecondaryButton");
        Assert.AreEqual(OccurrenceDeleteChoice.StopRepeating, await stopped);

        var cancelled = dialogs.AskDeleteOccurrenceAsync("Water plants");
        dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);
        DialogHost.Press(dialog, "CloseButton");
        Assert.AreEqual(OccurrenceDeleteChoice.Cancel, await cancelled);
    }

    [UITestMethod]
    public async Task Importing_asks_merge_or_replace_and_replace_is_confirmed_with_Cancel_as_the_default()
    {
        var ct = TestContext.CancellationToken;
        using var host = await DialogHost.OpenAsync(ct);
        IImportDialogs dialogs = new WinUiDialogs(new DialogService(() => host.Window));

        var merge = dialogs.ChooseImportModeAsync(@"C:\exports\taskdesk.json");
        var dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);
        Assert.AreEqual("Import", dialog.Title);
        StringAssert.StartsWith(DialogHost.Texts(dialog).Single(t => t.StartsWith("How", StringComparison.Ordinal)),
            "How should \"taskdesk.json\" be imported?");
        CollectionAssert.AreEqual(
            new[] { "Merge", "Replace everything…", "Cancel" }, DialogHost.Buttons(dialog).Select(b => b.Content).ToList());
        DialogHost.Press(dialog, "PrimaryButton");
        Assert.AreEqual(ImportMode.Merge, await merge);

        var replace = dialogs.ChooseImportModeAsync(@"C:\exports\taskdesk.json");
        dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);
        DialogHost.Press(dialog, "SecondaryButton");
        Assert.AreEqual(ImportMode.Replace, await replace);

        var cancelled = dialogs.ChooseImportModeAsync(@"C:\exports\taskdesk.json");
        dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);
        DialogHost.Press(dialog, "CloseButton");
        Assert.IsNull(await cancelled);

        var confirmed = dialogs.ConfirmReplaceAsync();
        dialog = await DialogHost.OpenDialogAsync(host.Window.Content.XamlRoot, ct);
        Assert.AreEqual("Replace all data", dialog.Title);
        CollectionAssert.AreEqual(
            new[] { "Replace everything", "Cancel" }, DialogHost.Buttons(dialog).Select(b => b.Content).ToList());
        Assert.AreEqual(ContentDialogButton.Close, dialog.DefaultButton);
        DialogHost.Press(dialog, "PrimaryButton");
        Assert.IsTrue(await confirmed);
    }
}
