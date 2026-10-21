using System.Text.Json;
using TaskDesk.Core.Export;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Import;

/// <summary>
/// Reads a JSON export (SPEC 9.1) for import and validates the whole file before anything is written
/// (SPEC 9.3): format, schema version, references, field ranges and duplicates, with the same rules the
/// app applies when the owner edits.
/// </summary>
public static class JsonImportFormat
{
    /// <summary>Parses and validates the whole file.</summary>
    /// <param name="json">The file bytes.</param>
    /// <returns>The document, safe to write.</returns>
    /// <exception cref="ImportException">The file is not a valid TaskDesk export of a known version.</exception>
    public static ExportDocument Read(ReadOnlySpan<byte> json)
    {
        CheckHeader(json);

        ExportDocument document;
        try
        {
            document = JsonExportFormat.Deserialize(json);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            throw new ImportException($"The TaskDesk export is damaged and cannot be read: {ex.Message}", ex);
        }

        document = Upgrade(document);
        Validate(document);
        return document;
    }

    // Brings a file of an older schema version to the current layout: a version 1 Task's single "reminderUtc"
    // becomes one Fixed Reminder. A version 2 file never carries "reminderUtc", so it is ignored there.
    private static ExportDocument Upgrade(ExportDocument document)
    {
        if (document.Tasks is null)
        {
            return document with { SchemaVersion = JsonExportFormat.SchemaVersion };
        }

        var version1 = document.SchemaVersion == 1;
        return document with
        {
            SchemaVersion = JsonExportFormat.SchemaVersion,
            Tasks =
            [
                .. document.Tasks.Select(task => task is null ? null! : task with
                {
                    Reminders = version1
                        ? task.ReminderUtc is { } at ? [new ExportReminder(ReminderKind.Fixed, at)] : []
                        : task.Reminders,
                    ReminderUtc = null,
                }),
            ],
        };
    }

    /// <summary>
    /// Compares List names the way the database does: SQLite's NOCASE collation folds ASCII letters only.
    /// </summary>
    public static StringComparer ListNameComparer { get; } = new AsciiCaseInsensitiveComparer();

    /// <summary>The Task an imported row becomes. Instants come back as UTC; text is normalized.</summary>
    /// <param name="task">The validated row.</param>
    /// <param name="listId">The List it goes into (a Merge may map it to a local List).</param>
    /// <returns>A new Task with its Reminders, without Tags or Subtasks.</returns>
    public static TaskItem ToTask(ExportTask task, Guid listId) => new()
    {
        Id = task.Id,
        ListId = listId,
        Title = TaskTitle.Normalize(task.Title),
        Notes = TaskNotes.Normalize(task.Notes),
        Priority = task.Priority,
        DueDate = task.DueDate,
        DueTime = task.DueTime,
        NotifyAtDue = task.NotifyAtDue,
        Reminders =
        [
            .. task.Reminders.Select(r => new Reminder
            {
                Id = Guid.CreateVersion7(),
                TaskId = task.Id,
                Kind = r.Kind,
                AtUtc = Utc(r.AtUtc),
                Amount = r.Amount,
                Unit = r.Unit,
            }),
        ],
        SnoozedUntilUtc = Utc(task.SnoozedUntilUtc),
        IsCompleted = task.IsCompleted,
        CompletedUtc = Utc(task.CompletedUtc),
        SortOrder = task.SortOrder,
        SeriesId = task.SeriesId,
        NextOccurrenceId = task.NextOccurrenceId,
        Recurrence = task.Recurrence is null ? null : ToRecurrence(task.Recurrence),
        CreatedUtc = Utc(task.CreatedUtc),
        UpdatedUtc = Utc(task.UpdatedUtc),
    };

    /// <summary>The List an imported row becomes.</summary>
    public static TaskList ToList(ExportList list) => new()
    {
        Id = list.Id,
        Name = ListName.Normalize(list.Name),
        ColorHex = ListColor.Normalize(list.ColorHex),
        SortOrder = list.SortOrder,
        IsDefault = list.IsDefault,
        CreatedUtc = Utc(list.CreatedUtc),
    };

    /// <summary>The Tag an imported row becomes.</summary>
    public static Tag ToTag(ExportTag tag) => new()
    {
        Id = tag.Id,
        Name = tag.Name,
        NormalizedName = TagName.Normalize(tag.Name),
        ColorHex = tag.ColorHex,
    };

    /// <summary>The Subtask an imported row becomes.</summary>
    public static Subtask ToSubtask(ExportSubtask subtask, Guid id, Guid taskId) => new()
    {
        Id = id,
        TaskId = taskId,
        Title = TaskTitle.Normalize(subtask.Title),
        IsCompleted = subtask.IsCompleted,
        SortOrder = subtask.SortOrder,
    };

    public static Recurrence ToRecurrence(ExportRecurrence r) => new()
    {
        Frequency = r.Frequency,
        Interval = r.Interval,
        DaysOfWeek = (r.DaysOfWeek ?? []).Aggregate(RecurrenceDays.None, (all, day) => all | day),
        Month = r.Month,
        DayOfMonth = r.DayOfMonth,
        Anchor = r.Anchor,
        EndDate = r.EndDate,
    };

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value,
    };

    private static DateTime? Utc(DateTime? value) => value is null ? null : Utc(value.Value);

    private static void CheckHeader(ReadOnlySpan<byte> json)
    {
        string? format;
        JsonElement version;
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ImportException("The file is not a TaskDesk export.");
            }

            format = root.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.String
                ? f.GetString()
                : null;
            version = root.TryGetProperty("schemaVersion", out var v) ? v.Clone() : default;
        }
        catch (JsonException ex)
        {
            throw new ImportException("The file is not valid JSON, so it is not a TaskDesk export.", ex);
        }

        if (format != JsonExportFormat.FormatName)
        {
            throw new ImportException("The file is not a TaskDesk export.");
        }

        if (version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var number)
            || number is < 1 or > JsonExportFormat.SchemaVersion)
        {
            var shown = version.ValueKind == JsonValueKind.Undefined ? "none" : version.GetRawText();
            throw new ImportException(
                $"The export has schema version {shown}; this version of TaskDesk reads schema versions 1 to "
                + $"{JsonExportFormat.SchemaVersion}.");
        }
    }

    private static void Validate(ExportDocument document)
    {
        if (document.Lists is null || document.Tags is null || document.Tasks is null
            || document.Lists.Any(l => l is null) || document.Tags.Any(t => t is null) || document.Tasks.Any(t => t is null))
        {
            throw Damaged("\"lists\", \"tags\" and \"tasks\" must all be present.");
        }

        ValidateLists(document.Lists);
        ValidateTags(document.Tags);

        var listIds = document.Lists.Select(l => l.Id).ToHashSet();
        var tagIds = document.Tags.Select(t => t.Id).ToHashSet();
        var taskIds = new HashSet<Guid>();
        var subtaskIds = new HashSet<Guid>();
        foreach (var task in document.Tasks)
        {
            Require(taskIds.Add(task.Id), $"Task {task.Id} appears more than once.");
            Require(listIds.Contains(task.ListId), $"Task {task.Id} is in List {task.ListId}, which is not in the file.");
            ValidateTask(task);

            Require(task.TagIds is not null, $"Task {task.Id} has no \"tagIds\".");
            Require(task.TagIds!.Distinct().Count() == task.TagIds!.Count, $"Task {task.Id} has the same Tag twice.");
            foreach (var tagId in task.TagIds!)
            {
                Require(tagIds.Contains(tagId), $"Task {task.Id} has Tag {tagId}, which is not in the file.");
            }

            Require(task.Subtasks is not null && task.Subtasks.All(s => s is not null), $"Task {task.Id} has no \"subtasks\".");
            foreach (var subtask in task.Subtasks!)
            {
                Require(subtaskIds.Add(subtask.Id), $"Subtask {subtask.Id} appears more than once.");
                Require(TaskTitle.IsValid(subtask.Title),
                    $"Subtask {subtask.Id} needs a title of 1 to {TaskTitle.MaxLength} characters.");
            }
        }
    }

    private static void ValidateLists(IReadOnlyList<ExportList> lists)
    {
        Require(lists.Count(l => l.IsDefault) == 1, "The file must have exactly one Default List.");
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(ListNameComparer);
        foreach (var list in lists)
        {
            Require(ids.Add(list.Id), $"List {list.Id} appears more than once.");
            Require(ListName.IsValid(list.Name), $"List {list.Id} needs a name of 1 to {ListName.MaxLength} characters.");
            Require(names.Add(list.Name.Trim()), $"Two Lists are named \"{list.Name.Trim()}\".");
            Require(ListColor.IsValid(list.ColorHex), $"List \"{list.Name}\" has a colour that is not #RRGGBB.");
        }
    }

    private static void ValidateTags(IReadOnlyList<ExportTag> tags)
    {
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            Require(ids.Add(tag.Id), $"Tag {tag.Id} appears more than once.");
            Require(TagName.IsValid(tag.Name),
                $"Tag {tag.Id} needs a name of 1 to {TagName.MaxLength} characters, no spaces and no leading #.");
            Require(names.Add(TagName.Normalize(tag.Name)), $"Two Tags are named \"{tag.Name}\".");
            Require(ListColor.IsValid(tag.ColorHex), $"Tag \"{tag.Name}\" has a colour that is not #RRGGBB.");
        }
    }

    private static void ValidateTask(ExportTask task)
    {
        Require(TaskTitle.IsValid(task.Title), $"Task {task.Id} needs a title of 1 to {TaskTitle.MaxLength} characters.");
        Require(TaskNotes.IsValid(task.Notes), $"Task {task.Id} has notes longer than {TaskNotes.MaxLength} characters.");
        Require(Enum.IsDefined(task.Priority), $"Task {task.Id} has an unknown priority.");
        Require(task.DueTime is null || task.DueDate is not null, $"Task {task.Id} has a Due time without a Due date.");
        Require(task.Reminders is not null && task.Reminders.All(r => r is not null), $"Task {task.Id} has no \"reminders\".");
        foreach (var reminder in task.Reminders!)
        {
            var value = new ReminderValue(reminder.Kind, Utc(reminder.AtUtc), reminder.Amount, reminder.Unit);
            Require(
                value is { Kind: ReminderKind.Fixed, AtUtc: not null, Amount: null, Unit: null }
                    || ReminderCalculator.IsValidBeforeDue(value),
                $"Task {task.Id} has a Reminder that is neither a Fixed Reminder with \"atUtc\" nor a BeforeDue "
                + $"Reminder with an \"amount\" from {ReminderValue.MinAmount} to {ReminderValue.MaxAmount} and a \"unit\".");
            Require(reminder.Kind != ReminderKind.BeforeDue || task.DueDate is not null,
                $"Task {task.Id} has a BeforeDue Reminder but no Due date.");
        }

        Require(task.Reminders!.Distinct().Count() == task.Reminders!.Count,
            $"Task {task.Id} has the same Reminder twice.");
        if (task.Recurrence is null)
        {
            return;
        }

        Require(task.DueDate is not null, $"Task {task.Id} repeats but has no Due date.");
        Require(task.Recurrence.DaysOfWeek is not null, $"Task {task.Id} has a Recurrence without \"daysOfWeek\".");
        try
        {
            ToRecurrence(task.Recurrence).Validate();
        }
        catch (ArgumentException ex)
        {
            throw new ImportException($"Task {task.Id} has an invalid Recurrence: {ex.Message}", ex);
        }
    }

    private static void Require(bool condition, string problem)
    {
        if (!condition)
        {
            throw Damaged(problem);
        }
    }

    private static ImportException Damaged(string problem) =>
        new($"The TaskDesk export is damaged and cannot be imported. {problem}");

    private sealed class AsciiCaseInsensitiveComparer : StringComparer
    {
        public override int Compare(string? x, string? y) =>
            string.CompareOrdinal(Fold(x), Fold(y));

        public override bool Equals(string? x, string? y) => string.Equals(Fold(x), Fold(y), StringComparison.Ordinal);

        public override int GetHashCode(string obj) => Fold(obj)!.GetHashCode(StringComparison.Ordinal);

        private static string? Fold(string? value) =>
            value is null ? null : string.Create(value.Length, value, static (span, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                {
                    var c = source[i];
                    span[i] = c is >= 'a' and <= 'z' ? (char)(c - 32) : c;
                }
            });
    }
}
