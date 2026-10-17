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

public sealed record ExportTask(
    Guid Id,
    Guid ListId,
    string Title,
    string? Notes,
    Priority Priority,
    DateOnly? DueDate,
    TimeOnly? DueTime,
    DateTime? ReminderUtc,
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
    DateTime UpdatedUtc);

public sealed record ExportRecurrence(
    RecurrenceFrequency Frequency,
    int Interval,
    IReadOnlyList<RecurrenceDays> DaysOfWeek,
    int? Month,
    int? DayOfMonth,
    RecurrenceAnchor Anchor,
    DateOnly? EndDate);

public sealed record ExportSubtask(Guid Id, string Title, bool IsCompleted, double SortOrder);
