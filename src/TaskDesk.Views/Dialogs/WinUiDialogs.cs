using Microsoft.UI.Xaml;
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
/// <param name="dialogs">The dialog service every dialog opens through.</param>
/// <param name="pickImportFile">
/// Asks for the JSON file to import on the given window; the Windows file picker unless a test passes its own.
/// </param>
public sealed class WinUiDialogs(DialogService dialogs, Func<Window, Task<string?>>? pickImportFile = null)
    : IListDialogs, IOccurrenceDialogs, IImportDialogs, IExportDialogs
{
    private readonly Func<Window, Task<string?>> _pickImportFile = pickImportFile ?? PickJsonFileAsync;

    // The import under way: its file picker starts it, and its Merge/Replace choice and Replace confirmation open on
    // the window the picker opened on. Only the import's own steps read or replace it, and its last step clears it.
    private DialogFlow? _import;

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
        var flow = ImportFlow();
        ImportMode? mode = null;
        try
        {
            var result = await flow.ShowAsync(new DialogRequest("Import", body, ["Merge", "Replace everything…"]));
            mode = result.ChoiceIndex switch
            {
                0 => ImportMode.Merge,
                1 => ImportMode.Replace,
                _ => null,
            };
            return mode;
        }
        finally
        {
            // Only Replace has a further step: its confirmation.
            if (mode != ImportMode.Replace)
            {
                EndImport(flow);
            }
        }
    }

    public async Task<bool> ConfirmReplaceAsync()
    {
        const string body =
            "Replace deletes all your current lists, tags and tasks and replaces them with the file's data.\n\n"
            + "A backup of the current data is saved in the backups folder first.\n\nReplace everything now?";
        var flow = ImportFlow();
        try
        {
            return await flow.ConfirmAsync("Replace all data", body, "Replace everything", destructive: true);
        }
        finally
        {
            EndImport(flow);
        }
    }

    public async Task<string?> PickImportPathAsync()
    {
        var flow = _import = dialogs.BeginFlow();
        string? path = null;
        try
        {
            path = await flow.ShowOnOwnerAsync(_pickImportFile);
            return path;
        }
        finally
        {
            if (path is null)
            {
                EndImport(flow);
            }
        }
    }

    // The import has no further step: let its window go, so a closed Settings window is not kept alive. A later
    // import that has started meanwhile keeps its own flow.
    private void EndImport(DialogFlow flow)
    {
        if (ReferenceEquals(_import, flow))
        {
            _import = null;
        }
    }

    private static async Task<string?> PickJsonFileAsync(Window owner)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    // A step asked for without the import's file picker first opens on the active window.
    private DialogFlow ImportFlow() => _import ?? dialogs.BeginFlow();

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
        return await dialogs.ShowOnOwnerAsync(async owner =>
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));
            var file = await picker.PickSaveFileAsync();
            return file?.Path;
        });
    }
}
