using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Export;

/// <summary>Reads and writes the JSON export of SPEC 9.1 (the file Import reads back).</summary>
public static class JsonExportFormat
{
    public const string FormatName = "taskdesk-export";

    /// <summary>The version of this file layout; bump it only with a breaking change to the layout.</summary>
    public const int SchemaVersion = 2;

    private static readonly RecurrenceDays[] WeekDays =
    [
        RecurrenceDays.Monday, RecurrenceDays.Tuesday, RecurrenceDays.Wednesday, RecurrenceDays.Thursday,
        RecurrenceDays.Friday, RecurrenceDays.Saturday, RecurrenceDays.Sunday,
    ];

    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Tasks in a deterministic order: by their List's order, then manual order, then id.</summary>
    public static IReadOnlyList<TaskItem> OrderedTasks(ExportData data)
    {
        var listRank = OrderedLists(data).Select((l, i) => (l.Id, i)).ToDictionary(x => x.Id, x => x.i);
        return
        [
            .. data.Tasks
                .OrderBy(t => listRank.GetValueOrDefault(t.ListId, int.MaxValue))
                .ThenBy(t => t.SortOrder)
                .ThenBy(t => t.Id),
        ];
    }

    /// <summary>Builds the document with a deterministic ordering.</summary>
    public static ExportDocument ToDocument(ExportData data, DateTime exportedUtc, string appVersion) => new(
        FormatName,
        SchemaVersion,
        Utc(exportedUtc),
        appVersion,
        [.. OrderedLists(data).Select(l => new ExportList(
            l.Id, l.Name, l.ColorHex, l.SortOrder, l.IsDefault, Utc(l.CreatedUtc)))],
        [.. data.Tags.OrderBy(t => t.NormalizedName, StringComparer.Ordinal).ThenBy(t => t.Id)
            .Select(t => new ExportTag(t.Id, t.Name, t.ColorHex))],
        [.. OrderedTasks(data).Select(ToExportTask)]);

    public static byte[] Serialize(ExportData data, DateTime exportedUtc, string appVersion) =>
        Serialize(ToDocument(data, exportedUtc, appVersion));

    /// <summary>UTF-8 without a BOM, indented, camelCase properties, enums as strings.</summary>
    public static byte[] Serialize(ExportDocument document) =>
        JsonSerializer.SerializeToUtf8Bytes(document, Options);

    /// <exception cref="JsonException">The bytes are not a valid export document.</exception>
    public static ExportDocument Deserialize(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<ExportDocument>(json, Options)
        ?? throw new JsonException("The file is empty.");

    private static IEnumerable<TaskList> OrderedLists(ExportData data) => data.Lists
        .OrderBy(l => l.SortOrder).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.Id);

    private static ExportTask ToExportTask(TaskItem t) => new(
        t.Id,
        t.ListId,
        t.Title,
        t.Notes,
        t.Priority,
        t.DueDate,
        t.DueTime,
        [.. OrderedReminders(t).Select(r => new ExportReminder(r.Kind, Utc(r.AtUtc), r.Amount, r.Unit))],
        Utc(t.SnoozedUntilUtc),
        t.IsCompleted,
        Utc(t.CompletedUtc),
        t.SortOrder,
        t.SeriesId,
        t.NextOccurrenceId,
        t.Recurrence is null ? null : ToExportRecurrence(t.Recurrence),
        [.. OrderedTags(t).Select(tag => tag.Id)],
        [.. OrderedSubtasks(t).Select(s => new ExportSubtask(s.Id, s.Title, s.IsCompleted, s.SortOrder))],
        Utc(t.CreatedUtc),
        Utc(t.UpdatedUtc));

    /// <summary>A Task's Tags in the same order the export lists them.</summary>
    internal static IEnumerable<TaskDesk.Core.Tags.Tag> OrderedTags(TaskItem task) =>
        task.Tags.OrderBy(tag => tag.NormalizedName, StringComparer.Ordinal).ThenBy(tag => tag.Id);

    /// <summary>
    /// A Task's Reminders in the order they fire, as the export lists them. The Due is read in UTC, so the order
    /// is the same on every machine.
    /// </summary>
    internal static IEnumerable<TaskDesk.Core.Reminders.Reminder> OrderedReminders(TaskItem task) =>
        task.Reminders.OrderBy(r => TaskDesk.Core.Reminders.ReminderCalculator.MomentOf(r, task)).ThenBy(r => r.Id);

    /// <summary>A Task's Subtasks in the same order the export lists them.</summary>
    internal static IEnumerable<Subtask> OrderedSubtasks(TaskItem task) =>
        task.Subtasks.OrderBy(s => s.SortOrder).ThenBy(s => s.Id);

    private static ExportRecurrence ToExportRecurrence(Recurrence r) => new(
        r.Frequency,
        r.Interval,
        [.. WeekDays.Where(d => r.DaysOfWeek.HasFlag(d))],
        r.Month,
        r.DayOfMonth,
        r.Anchor,
        r.EndDate);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? Utc(DateTime? value) => value is null ? null : Utc(value.Value);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,

            // The same bytes on every machine, whatever the platform line ending.
            NewLine = "\n",
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

            // Keep accents (ç, ã) as written instead of ç escapes.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new TimeOnlyMinutesConverter());
        return options;
    }

    /// <summary>Writes a due time as "HH:mm" (SPEC 9.1), keeping seconds only when there are any.</summary>
    private sealed class TimeOnlyMinutesConverter : JsonConverter<TimeOnly>
    {
        public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString();
            if (!TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                throw new JsonException($"'{text}' is not a valid time.");
            }

            return time;
        }

        public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Ticks % TimeSpan.TicksPerMinute == 0
                ? value.ToString("HH:mm", CultureInfo.InvariantCulture)
                : value.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture));
    }
}
