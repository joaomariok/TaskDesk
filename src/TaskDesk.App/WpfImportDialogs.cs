using System.IO;
using Microsoft.Win32;
using TaskDesk.App.Dialogs;
using TaskDesk.Core.Import;
using TaskDesk.Presentation;

namespace TaskDesk.App;

/// <summary>WPF implementation of the import dialogs: Open file, a Merge/Replace choice and a Replace warning.</summary>
internal sealed class WpfImportDialogs : IImportDialogs
{
    public string? PickImportPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import a TaskDesk JSON export",
            DefaultExt = ".json",
            Filter = "JSON file (*.json)|*.json",
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public ImportMode? ChooseImportMode(string path)
    {
        var body = $"How should \"{Path.GetFileName(path)}\" be imported?\n\n"
            + "Merge: add the lists, tags and tasks that are not here yet. Nothing here is changed or deleted.\n\n"
            + "Replace: delete ALL current lists, tags and tasks and use the file's data instead. "
            + "A backup is taken first.";
        var result = FluentDialogService.Show(new FluentDialogRequest("Import", body, ["Merge", "Replace everything…"]));
        return result.ChoiceIndex switch
        {
            0 => ImportMode.Merge,
            1 => ImportMode.Replace,
            _ => null,
        };
    }

    public bool ConfirmReplace()
    {
        const string body =
            "Replace deletes all your current lists, tags and tasks and replaces them with the file's data.\n\n"
            + "A backup of the current data is saved in the backups folder first.\n\nReplace everything now?";
        return FluentDialogService.Confirm("Replace all data", body, "Replace everything");
    }
}
