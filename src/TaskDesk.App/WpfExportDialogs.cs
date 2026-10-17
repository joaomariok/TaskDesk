using Microsoft.Win32;
using TaskDesk.Core.Export;
using TaskDesk.Presentation;

namespace TaskDesk.App;

/// <summary>WPF implementation of the export file dialog: the standard Save As dialog.</summary>
internal sealed class WpfExportDialogs : IExportDialogs
{
    // The WPF dialog is modal and synchronous; the result is returned as a completed task.
    public Task<string?> PickExportPathAsync(ExportFormat format, string suggestedFileName)
    {
        var json = format == ExportFormat.Json;
        var dialog = new SaveFileDialog
        {
            Title = json ? "Export to JSON" : "Export to CSV",
            FileName = suggestedFileName,
            DefaultExt = json ? ".json" : ".csv",
            Filter = json ? "JSON file (*.json)|*.json" : "CSV file (*.csv)|*.csv",
            OverwritePrompt = true,
        };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }
}
