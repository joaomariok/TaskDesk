using System.Text;
using TaskDesk.Core.Export;
using TaskDesk.Core.Import;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public sealed class JsonImportFormatTests
{
    private static readonly Guid InboxId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkId = new("00000000-0000-0000-0000-000000000002");
    private static readonly Guid TagId = new("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid TaskId = new("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid SubtaskId = new("00000000-0000-0000-0000-0000000000d1");
    private static readonly DateTime Created = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ReminderAt = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static ExportList Inbox => new(InboxId, "Inbox", null, 0, true, Created);

    private static ExportTag WorkTag => new(TagId, "work", "#3366FF");

    private static ExportTask ValidTask => new(
        TaskId, InboxId, "Pay rent", null, Priority.High, new DateOnly(2026, 10, 20), new TimeOnly(14, 0),
        [new ExportReminder(ReminderKind.Fixed, ReminderAt)], null, false, null, 1.0, null, null,
        new ExportRecurrence(RecurrenceFrequency.Weekly, 1, [RecurrenceDays.Monday], null, null, RecurrenceAnchor.DueDate, null),
        [TagId], [new ExportSubtask(SubtaskId, "draft", false, 0)], Created, Created);

    private static ExportDocument Valid => new(
        JsonExportFormat.FormatName, JsonExportFormat.SchemaVersion, Created, "1.0.0",
        [Inbox, new ExportList(WorkId, "Work", "#3366FF", 1, false, Created)],
        [WorkTag],
        [ValidTask]);

    private static ImportException Reject(string json) =>
        Assert.Throws<ImportException>(() => JsonImportFormat.Read(Encoding.UTF8.GetBytes(json)));

    private static ImportException Reject(ExportDocument document) =>
        Assert.Throws<ImportException>(() => JsonImportFormat.Read(JsonExportFormat.Serialize(document)));

    private static ExportDocument WithTask(Func<ExportTask, ExportTask> change) => Valid with { Tasks = [change(ValidTask)] };

    [Fact]
    public void A_valid_export_is_read_back_unchanged()
    {
        var bytes = JsonExportFormat.Serialize(Valid);

        var document = JsonImportFormat.Read(bytes);

        Assert.Equal(bytes, JsonExportFormat.Serialize(document));
    }

    [Fact]
    public void A_file_with_another_format_is_rejected_with_a_clear_message()
    {
        var error = Reject("""{ "format": "todo-app", "schemaVersion": 1, "lists": [], "tags": [], "tasks": [] }""");

        Assert.Contains("not a TaskDesk export", error.Message);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1, 2]")]
    [InlineData("")]
    public void A_file_that_is_not_a_json_object_is_rejected(string json)
    {
        var error = Reject(json);

        Assert.Contains("not a TaskDesk export", error.Message);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("\"1\"")]
    [InlineData("null")]
    public void An_unknown_schema_version_is_rejected_with_a_clear_message(string version)
    {
        var error = Reject($$"""{ "format": "taskdesk-export", "schemaVersion": {{version}}, "lists": [], "tags": [], "tasks": [] }""");

        Assert.Contains("schema version", error.Message);
    }

    [Fact]
    public void A_version_1_reminderUtc_is_read_as_one_Fixed_Reminder()
    {
        var json = Encoding.UTF8.GetString(JsonExportFormat.Serialize(Valid with
        {
            Tasks = [ValidTask, ValidTask with { Id = Guid.NewGuid(), Reminders = [], Subtasks = [] }],
        }));
        // A version 1 export: "reminderUtc" on each Task, no "reminders".
        var version1 = json
            .Replace("\"schemaVersion\": 2", "\"schemaVersion\": 1", StringComparison.Ordinal)
            .Replace(
                "\"reminders\": [\n        {\n          \"kind\": \"Fixed\",\n          \"atUtc\": \"2026-10-20T12:00:00Z\"\n        }\n      ]",
                "\"reminderUtc\": \"2026-10-20T12:00:00Z\"",
                StringComparison.Ordinal)
            .Replace("\"reminders\": []", "\"reminderUtc\": null", StringComparison.Ordinal);
        Assert.DoesNotContain("\"reminders\"", version1, StringComparison.Ordinal);

        var read = JsonImportFormat.Read(Encoding.UTF8.GetBytes(version1));

        Assert.Equal(JsonExportFormat.SchemaVersion, read.SchemaVersion);
        Assert.Equal([new ExportReminder(ReminderKind.Fixed, ReminderAt)], read.Tasks[0].Reminders);
        Assert.Empty(read.Tasks[1].Reminders);
        Assert.All(read.Tasks, t => Assert.Null(t.ReminderUtc));
        Assert.Equal(json, Encoding.UTF8.GetString(JsonExportFormat.Serialize(read)));
    }

    [Fact]
    public void A_version_2_file_needs_reminders_on_every_Task()
    {
        var json = Encoding.UTF8.GetString(JsonExportFormat.Serialize(WithTask(t => t with { Reminders = [] })))
            .Replace("\"reminders\": [],", "", StringComparison.Ordinal);

        var error = Reject(json);

        Assert.Contains("reminders", error.Message);
    }

    [Fact]
    public void A_structurally_broken_export_is_rejected_as_malformed()
    {
        var error = Reject("""{ "format": "taskdesk-export", "schemaVersion": 1, "lists": "oops", "tags": [], "tasks": [] }""");

        Assert.Contains("damaged", error.Message);
    }

    [Fact]
    public void Missing_collections_are_rejected()
    {
        var error = Reject("""{ "format": "taskdesk-export", "schemaVersion": 1, "lists": [], "tasks": [] }""");

        Assert.Contains("damaged", error.Message);
    }

    [Fact]
    public void A_task_in_a_list_that_is_not_in_the_file_is_rejected()
    {
        var error = Reject(WithTask(t => t with { ListId = Guid.NewGuid() }));

        Assert.Contains("List", error.Message);
    }

    [Fact]
    public void A_task_with_a_tag_that_is_not_in_the_file_is_rejected()
    {
        var error = Reject(WithTask(t => t with { TagIds = [Guid.NewGuid()] }));

        Assert.Contains("Tag", error.Message);
    }

    [Fact]
    public void A_dangling_next_occurrence_is_allowed_because_it_is_not_a_foreign_key()
    {
        // ADR 0001 / SPEC 4.1: the next Occurrence may have been deleted after it was produced.
        var document = WithTask(t => t with { SeriesId = Guid.NewGuid(), NextOccurrenceId = Guid.NewGuid() });

        var read = JsonImportFormat.Read(JsonExportFormat.Serialize(document));

        Assert.Equal(document.Tasks[0].NextOccurrenceId, read.Tasks[0].NextOccurrenceId);
    }

    public static TheoryData<string> InvalidDocuments() =>
    [
        "no default list", "two default lists", "duplicate list id", "duplicate list name", "blank list name",
        "long list name", "bad list colour", "duplicate tag id", "duplicate tag name", "tag with space",
        "tag with hash", "bad tag colour", "duplicate task id", "blank title", "long title", "long notes",
        "unknown priority", "due time without date", "recurrence without date", "weekly without days",
        "interval zero", "feb 30", "duplicate subtask id", "blank subtask title", "tag twice on task",
        "null tag ids", "null subtasks", "null list name", "null reminders", "reminder without instant",
        "same reminder twice",
    ];

    [Theory]
    [MemberData(nameof(InvalidDocuments))]
    public void Invalid_field_values_and_duplicates_are_rejected(string problem)
    {
        var task = ValidTask;
        var document = problem switch
        {
            "no default list" => Valid with { Lists = [Inbox with { IsDefault = false }], Tasks = [] },
            "two default lists" => Valid with { Lists = [Inbox, new ExportList(WorkId, "Work", null, 1, true, Created)] },
            "duplicate list id" => Valid with { Lists = [Inbox, new ExportList(InboxId, "Work", null, 1, false, Created)] },
            "duplicate list name" => Valid with { Lists = [Inbox, new ExportList(WorkId, "INBOX", null, 1, false, Created)] },
            "blank list name" => Valid with { Lists = [Inbox with { Name = "   " }] },
            "long list name" => Valid with { Lists = [Inbox with { Name = new string('a', 101) }] },
            "null list name" => Valid with { Lists = [Inbox with { Name = null! }] },
            "bad list colour" => Valid with { Lists = [Inbox with { ColorHex = "red" }] },
            "duplicate tag id" => Valid with { Tags = [WorkTag, WorkTag with { Name = "home" }] },
            "duplicate tag name" => Valid with { Tags = [WorkTag, new ExportTag(Guid.NewGuid(), "WORK", null)] },
            "tag with space" => Valid with { Tags = [WorkTag with { Name = "two words" }] },
            "tag with hash" => Valid with { Tags = [WorkTag with { Name = "#work" }] },
            "bad tag colour" => Valid with { Tags = [WorkTag with { ColorHex = "#12345" }] },
            "duplicate task id" => Valid with { Tasks = [task, task with { Subtasks = [] }] },
            "blank title" => WithTask(t => t with { Title = " " }),
            "long title" => WithTask(t => t with { Title = new string('a', 501) }),
            "long notes" => WithTask(t => t with { Notes = new string('a', 10_001) }),
            "unknown priority" => WithTask(t => t with { Priority = (Priority)7 }),
            "due time without date" => WithTask(t => t with { DueDate = null, Recurrence = null }),
            "recurrence without date" => WithTask(t => t with { DueDate = null, DueTime = null }),
            "weekly without days" => WithTask(t => t with { Recurrence = t.Recurrence! with { DaysOfWeek = [] } }),
            "interval zero" => WithTask(t => t with { Recurrence = t.Recurrence! with { Interval = 0 } }),
            "feb 30" => WithTask(t => t with
            {
                Recurrence = new ExportRecurrence(RecurrenceFrequency.Yearly, 1, [], 2, 30, RecurrenceAnchor.DueDate, null),
            }),
            "duplicate subtask id" => WithTask(t => t with { Subtasks = [t.Subtasks[0], t.Subtasks[0] with { Title = "b" }] }),
            "blank subtask title" => WithTask(t => t with { Subtasks = [t.Subtasks[0] with { Title = "" }] }),
            "tag twice on task" => WithTask(t => t with { TagIds = [TagId, TagId] }),
            "null tag ids" => WithTask(t => t with { TagIds = null! }),
            "null subtasks" => WithTask(t => t with { Subtasks = null! }),
            "null reminders" => WithTask(t => t with { Reminders = null! }),
            "reminder without instant" => WithTask(t => t with { Reminders = [new ExportReminder(ReminderKind.Fixed, null)] }),
            "same reminder twice" => WithTask(t => t with { Reminders = [t.Reminders[0], t.Reminders[0]] }),
            _ => throw new ArgumentOutOfRangeException(nameof(problem)),
        };

        var error = Reject(document);

        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }
}
