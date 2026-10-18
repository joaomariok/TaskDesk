using Microsoft.UI.Xaml;
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

    [UITestMethod]
    public async Task An_import_keeps_its_steps_on_the_picker_s_window_and_lets_it_go_when_it_ends()
    {
        var ct = TestContext.CancellationToken;
        using var settings = await DialogHost.OpenAsync(ct);
        using var main = await DialogHost.OpenAsync(ct);
        var active = settings.Window;
        var pickedOn = new List<Window>();
        const string file = @"C:\exports\taskdesk.json";
        IImportDialogs dialogs = new WinUiDialogs(new DialogService(() => active), owner =>
        {
            pickedOn.Add(owner);
            return Task.FromResult<string?>(file);
        });
        try
        {
            // Replace, then Cancel on its confirmation; the owner clicks the main window after picking the file.
            Assert.AreEqual(file, await dialogs.PickImportPathAsync());
            active = main.Window;
            var mode = dialogs.ChooseImportModeAsync(file);
            DialogHost.Press(await DialogHost.OpenDialogAsync(settings.Window.Content.XamlRoot, ct), "SecondaryButton");
            Assert.AreEqual(ImportMode.Replace, await mode);
            var confirmed = dialogs.ConfirmReplaceAsync();
            DialogHost.Press(await DialogHost.OpenDialogAsync(settings.Window.Content.XamlRoot, ct), "CloseButton");
            Assert.IsFalse(await confirmed);
            await AssertImportEndedAsync(dialogs, main, file, ct);

            // Merge ends the import at the mode choice.
            active = settings.Window;
            Assert.AreEqual(file, await dialogs.PickImportPathAsync());
            active = main.Window;
            mode = dialogs.ChooseImportModeAsync(file);
            DialogHost.Press(await DialogHost.OpenDialogAsync(settings.Window.Content.XamlRoot, ct), "PrimaryButton");
            Assert.AreEqual(ImportMode.Merge, await mode);
            await AssertImportEndedAsync(dialogs, main, file, ct);

            CollectionAssert.AreEqual(new[] { settings.Window, settings.Window }, pickedOn);
        }
        finally
        {
            DialogHost.OpenDialog(settings.Window.Content.XamlRoot)?.Hide();
            DialogHost.OpenDialog(main.Window.Content.XamlRoot)?.Hide();
        }
    }

    // Once an import has ended it holds no window: a step asked for on its own opens on the window active now.
    private static async Task AssertImportEndedAsync(
        IImportDialogs dialogs, DialogHost active, string file, CancellationToken ct)
    {
        await Task.Delay(100, ct); // the closed dialog leaves its window's popup layer
        var stray = dialogs.ChooseImportModeAsync(file);
        DialogHost.Press(await DialogHost.OpenDialogAsync(active.Window.Content.XamlRoot, ct), "CloseButton");
        Assert.IsNull(await stray);
    }
}
