using TaskDesk.Core.Export;

namespace TaskDesk.Presentation;

/// <summary>The file dialog the Export controls use; the WPF implementation lives in the App project.</summary>
public interface IExportDialogs
{
    /// <summary>Asks where to save an export.</summary>
    /// <param name="format">The format being exported, which decides the file type filter.</param>
    /// <param name="suggestedFileName">The file name to propose.</param>
    /// <returns>The chosen path, or null when the owner cancelled.</returns>
    string? PickExportPath(ExportFormat format, string suggestedFileName);
}
