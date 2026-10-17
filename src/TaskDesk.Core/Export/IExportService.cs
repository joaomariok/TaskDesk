namespace TaskDesk.Core.Export;

public enum ExportFormat
{
    /// <summary>Full fidelity; the format Import reads back (SPEC 9.1).</summary>
    Json,

    /// <summary>One row per Task, for spreadsheets; export-only (SPEC 9.2).</summary>
    Csv,
}

/// <summary>Use cases for exporting everything to a file.</summary>
public interface IExportService
{
    /// <summary>Writes every List, Tag and Task to <paramref name="path"/> as JSON (SPEC 9.1).</summary>
    /// <param name="path">The file to create or replace.</param>
    /// <param name="appVersion">Recorded in the file as <c>appVersion</c>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>A task that completes when the file is written.</returns>
    Task ExportJsonAsync(string path, string appVersion, CancellationToken cancellationToken = default);

    /// <summary>Writes one row per Task to <paramref name="path"/> as CSV with a BOM (SPEC 9.2).</summary>
    /// <param name="path">The file to create or replace.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>A task that completes when the file is written.</returns>
    Task ExportCsvAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Names the export file the save dialog proposes.</summary>
public static class ExportNaming
{
    public static string SuggestedFileName(DateTime localNow, ExportFormat format) =>
        $"taskdesk-export-{localNow:yyyyMMdd-HHmmss}.{(format == ExportFormat.Json ? "json" : "csv")}";
}
