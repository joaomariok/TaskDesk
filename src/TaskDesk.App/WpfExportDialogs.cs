using Microsoft.Win32;
using TaskDesk.Core.Export;
using TaskDesk.Presentation;

namespace TaskDesk.App;

/// <summary>WPF implementation of the export file dialog: the standard Save As dialog.</summary>
internal sealed class WpfExportDialogs : IExportDialogs
{
    public string? PickExportPath(ExportFormat format, string suggestedFileName)
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
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
