using System.Globalization;
using System.Text;

namespace TaskDesk.Core.Export;

/// <summary>Writes the CSV export of SPEC 9.2: one row per Task, RFC 4180 quoting, UTF-8 with a BOM.</summary>
public static class CsvExportFormat
{
    public static readonly IReadOnlyList<string> Columns =
    [
        "List", "Title", "Notes", "Priority", "DueDate", "DueTime", "Completed", "CompletedLocal",
        "Tags", "Subtasks", "Recurrence",
    ];

    /// <summary>Quotes a field when it contains a comma, quote, CR or LF; doubles inner quotes.</summary>
    /// <param name="field">The raw field text.</param>
    /// <returns>The field as it appears in the file.</returns>
    public static string Escape(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return "";
        }

        return field.AsSpan().IndexOfAny(",\"\r\n") < 0 ? field : "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>The CSV text (without BOM), rows separated by CRLF, in the same order as the JSON export.</summary>
    /// <param name="data">What to export.</param>
    /// <param name="localZone">The zone CompletedLocal is shown in.</param>
    /// <returns>The text.</returns>
    public static string Build(ExportData data, TimeZoneInfo localZone)
    {
        var listNames = data.Lists.ToDictionary(l => l.Id, l => l.Name);
        var text = new StringBuilder();
        AppendRow(text, Columns);
        foreach (var task in JsonExportFormat.OrderedTasks(data))
        {
            var completed = task.CompletedUtc is { } done
                ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(done, DateTimeKind.Utc), localZone)
                    .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : "";
            AppendRow(text,
            [
                listNames.GetValueOrDefault(task.ListId, ""),
                task.Title,
                task.Notes ?? "",
                task.Priority.ToString(),
                task.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                task.DueTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "",
                task.IsCompleted ? "true" : "false",
                completed,
                string.Join(' ', JsonExportFormat.OrderedTags(task).Select(t => t.Name)),
                string.Join("; ", JsonExportFormat.OrderedSubtasks(task).Select(s => (s.IsCompleted ? "[x] " : "") + s.Title)),
                task.Recurrence is null ? "" : RecurrenceText.Describe(task.Recurrence),
            ]);
        }

        return text.ToString();
    }

    /// <summary>The file bytes: a UTF-8 BOM first, so Excel opens accents correctly.</summary>
    /// <param name="data">What to export.</param>
    /// <param name="localZone">The zone CompletedLocal is shown in.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Serialize(ExportData data, TimeZoneInfo localZone) =>
        [.. Encoding.UTF8.GetPreamble(), .. new UTF8Encoding(false).GetBytes(Build(data, localZone))];

    private static void AppendRow(StringBuilder text, IEnumerable<string> fields) =>
        text.AppendJoin(',', fields.Select(Escape)).Append("\r\n");
}
