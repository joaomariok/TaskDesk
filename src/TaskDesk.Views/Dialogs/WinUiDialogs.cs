using TaskDesk.Core.Export;
using TaskDesk.Core.Import;
using TaskDesk.Presentation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TaskDesk.Views.Dialogs;

/// <summary>
/// The WinUI implementation of the Presentation dialog interfaces: confirmations and choices on the one
/// <see cref="DialogService"/>, with the WPF app's texts, and the Windows file pickers on the owner window.
/// </summary>
public sealed class WinUiDialogs(DialogService dialogs) : IListDialogs, IOccurrenceDialogs, IImportDialogs, IExportDialogs
{
    public Task<bool> ConfirmDeleteAsync(string listName, int taskCount)
    {
        var body = $"Delete the list \"{listName}\" and all its tasks ({taskCount} open)? This cannot be undone.";
        return dialogs.ConfirmAsync("Delete list", body, "Delete", destructive: true);
    }

    public async Task<string?> PromptNameAsync(string currentName)
    {
        var result = await dialogs.ShowAsync(
            new DialogRequest("Rename list", "Choose a new name for this list.", ["Rename"])
            {
                Input = new DialogInput("List name", currentName),
            });
        return result.IsCancelled ? null : result.Text;
    }

    public async Task<OccurrenceDeleteChoice> AskDeleteOccurrenceAsync(string taskTitle)
    {
        var body = $"\"{taskTitle}\" repeats. Delete only this occurrence (the next one is still created), "
            + "or stop repeating?";
        var result = await dialogs.ShowAsync(
            new DialogRequest("Delete repeating task", body, ["Delete this occurrence", "Stop repeating"]));
        return result.ChoiceIndex switch
        {
            0 => OccurrenceDeleteChoice.DeleteThisOccurrence,
            1 => OccurrenceDeleteChoice.StopRepeating,
            _ => OccurrenceDeleteChoice.Cancel,
        };
    }

    public async Task<ImportMode?> ChooseImportModeAsync(string path)
    {
        var body = $"How should \"{Path.GetFileName(path)}\" be imported?\n\n"
            + "Merge: add the lists, tags and tasks that are not here yet. Nothing here is changed or deleted.\n\n"
            + "Replace: delete ALL current lists, tags and tasks and use the file's data instead. "
            + "A backup is taken first.";
        var result = await dialogs.ShowAsync(new DialogRequest("Import", body, ["Merge", "Replace everything…"]));
        return result.ChoiceIndex switch
        {
            0 => ImportMode.Merge,
            1 => ImportMode.Replace,
            _ => null,
        };
    }

    public Task<bool> ConfirmReplaceAsync()
    {
        const string body =
            "Replace deletes all your current lists, tags and tasks and replaces them with the file's data.\n\n"
            + "A backup of the current data is saved in the backups folder first.\n\nReplace everything now?";
        return dialogs.ConfirmAsync("Replace all data", body, "Replace everything", destructive: true);
    }

    public async Task<string?> PickImportPathAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(dialogs.OwnerWindow()));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickExportPathAsync(ExportFormat format, string suggestedFileName)
    {
        var json = format == ExportFormat.Json;
        var extension = json ? ".json" : ".csv";
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName),
            DefaultFileExtension = extension,
        };
        picker.FileTypeChoices.Add(json ? "JSON file" : "CSV file", [extension]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(dialogs.OwnerWindow()));
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
