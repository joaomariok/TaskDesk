using System.Text.Json.Serialization;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Export;

// The JSON export document of SPEC 9.1. Property names are camelCased by JsonExportFormat.

public sealed record ExportDocument(
    string Format,
    int SchemaVersion,
    DateTime ExportedUtc,
    string AppVersion,
    IReadOnlyList<ExportList> Lists,
    IReadOnlyList<ExportTag> Tags,
    IReadOnlyList<ExportTask> Tasks);

public sealed record ExportList(
    Guid Id, string Name, string? ColorHex, int SortOrder, bool IsDefault, DateTime CreatedUtc);

public sealed record ExportTag(Guid Id, string Name, string? ColorHex);

/// <param name="Reminders">The Task's Reminders (schema version 2), the one that fires first first.</param>
/// <param name="ReminderUtc">
/// The single Reminder of a schema version 1 file, read only for import, which turns it into one Fixed Reminder.
/// Never written.
/// </param>
public sealed record ExportTask(
    Guid Id,
    Guid ListId,
    string Title,
    string? Notes,
    Priority Priority,
    DateOnly? DueDate,
    TimeOnly? DueTime,
    IReadOnlyList<ExportReminder> Reminders,
    DateTime? SnoozedUntilUtc,
    bool IsCompleted,
    DateTime? CompletedUtc,
    double SortOrder,
    Guid? SeriesId,
    Guid? NextOccurrenceId,
    ExportRecurrence? Recurrence,
    IReadOnlyList<Guid> TagIds,
    IReadOnlyList<ExportSubtask> Subtasks,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? ReminderUtc = null);

/// <summary>
/// A Reminder of a Task: its kind and, for a Fixed Reminder, its instant, or for a before-Due Reminder, its amount
/// and unit.
/// </summary>
public sealed record ExportReminder(
    ReminderKind Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? AtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Amount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReminderUnit? Unit = null);

public sealed record ExportRecurrence(
    RecurrenceFrequency Frequency,
    int Interval,
    IReadOnlyList<RecurrenceDays> DaysOfWeek,
    int? Month,
    int? DayOfMonth,
    RecurrenceAnchor Anchor,
    DateOnly? EndDate);

public sealed record ExportSubtask(Guid Id, string Title, bool IsCompleted, double SortOrder);
