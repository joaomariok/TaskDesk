using System.Text;
using System.Text.Json;
using TaskDesk.Core.Export;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public sealed class JsonExportFormatTests
{
    private static readonly DateTime Exported = new(2026, 10, 16, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_format_name_and_schema_version_are_pinned()
    {
        // Changing either breaks every export already on disk and the Import that reads them.
        Assert.Equal("taskdesk-export", JsonExportFormat.FormatName);
        Assert.Equal(1, JsonExportFormat.SchemaVersion);

        using var json = Parse(new ExportData([], [], []));
        Assert.Equal("taskdesk-export", json.RootElement.GetProperty("format").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("1.0.0", json.RootElement.GetProperty("appVersion").GetString());
        Assert.Equal("2026-10-16T20:00:00Z", json.RootElement.GetProperty("exportedUtc").GetString());
    }

    [Fact]
    public void The_output_is_utf8_without_a_bom_indented_with_literal_accents()
    {
        var list = new TaskList { Id = Guid.NewGuid(), Name = "Ação", IsDefault = true };
        var bytes = JsonExportFormat.Serialize(new ExportData([list], [], []), Exported, "1.0.0");

        Assert.Equal((byte)'{', bytes[0]);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\n  \"format\": \"taskdesk-export\"", text);
        Assert.Contains("\"name\": \"Ação\"", text);
        Assert.DoesNotContain("\r", text);
    }

    [Theory]
    [InlineData(14, 0, 0, "14:00")]
    [InlineData(0, 5, 0, "00:05")]
    [InlineData(23, 59, 0, "23:59")]
    [InlineData(9, 30, 15, "09:30:15")]
    public void Due_time_is_written_as_hours_and_minutes(int hour, int minute, int second, string expected)
    {
        var data = OneTask(new TaskItem { DueDate = new DateOnly(2026, 10, 20), DueTime = new TimeOnly(hour, minute, second) });

        using var json = Parse(data);

        Assert.Equal(expected, FirstTask(json).GetProperty("dueTime").GetString());
    }

    [Fact]
    public void Enums_are_strings_and_days_of_week_is_a_list_of_names()
    {
        var data = OneTask(new TaskItem
        {
            Priority = Priority.High,
            DueDate = new DateOnly(2026, 10, 20),
            Recurrence = Recurrence.Weekly(RecurrenceDays.Thursday | RecurrenceDays.Monday),
        });

        using var json = Parse(data);
        var task = FirstTask(json);
        var recurrence = task.GetProperty("recurrence");

        Assert.Equal("High", task.GetProperty("priority").GetString());
        Assert.Equal("Weekly", recurrence.GetProperty("frequency").GetString());
        Assert.Equal("DueDate", recurrence.GetProperty("anchor").GetString());
        Assert.Equal(
            ["Monday", "Thursday"],
            recurrence.GetProperty("daysOfWeek").EnumerateArray().Select(d => d.GetString()));
        Assert.Equal(JsonValueKind.Null, recurrence.GetProperty("month").ValueKind);
        Assert.Equal(JsonValueKind.Null, recurrence.GetProperty("dayOfMonth").ValueKind);
        Assert.Equal(JsonValueKind.Null, recurrence.GetProperty("endDate").ValueKind);
    }

    [Fact]
    public void A_yearly_recurrence_keeps_its_month_and_a_task_without_one_has_a_null_recurrence()
    {
        var yearly = new TaskItem
        {
            Id = Guid.NewGuid(),
            DueDate = new DateOnly(2026, 3, 14),
            Recurrence = Recurrence.Yearly(3, 14),
        };
        var plain = new TaskItem { Id = Guid.NewGuid() };
        var data = new ExportData([], [], [yearly, plain]);

        using var json = Parse(data);
        var tasks = json.RootElement.GetProperty("tasks").EnumerateArray().ToList();
        var byId = tasks.ToDictionary(t => t.GetProperty("id").GetGuid());

        Assert.Equal(3, byId[yearly.Id].GetProperty("recurrence").GetProperty("month").GetInt32());
        Assert.Equal(JsonValueKind.Null, byId[plain.Id].GetProperty("recurrence").ValueKind);
    }

    [Fact]
    public void Tag_ids_and_subtasks_are_in_a_deterministic_order()
    {
        var b = new TaskDesk.Core.Tags.Tag { Id = Guid.NewGuid(), Name = "b", NormalizedName = "B" };
        var a = new TaskDesk.Core.Tags.Tag { Id = Guid.NewGuid(), Name = "A", NormalizedName = "A" };
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Tags = [b, a],
            Subtasks =
            [
                new Subtask { Id = Guid.NewGuid(), Title = "two", SortOrder = 2 },
                new Subtask { Id = Guid.NewGuid(), Title = "one", SortOrder = 1 },
            ],
        };

        using var json = Parse(new ExportData([], [b, a], [task]));
        var exported = FirstTask(json);

        Assert.Equal(
            [a.Id, b.Id], exported.GetProperty("tagIds").EnumerateArray().Select(t => t.GetGuid()));
        Assert.Equal(
            ["one", "two"], exported.GetProperty("subtasks").EnumerateArray().Select(s => s.GetProperty("title").GetString()));
        Assert.Equal(["A", "b"], json.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
    }

    [Fact]
    public void A_written_document_reads_back_and_writes_the_same_bytes()
    {
        var tag = new TaskDesk.Core.Tags.Tag { Id = Guid.NewGuid(), Name = "work", NormalizedName = "WORK", ColorHex = "#3366FF" };
        var list = new TaskList { Id = Guid.NewGuid(), Name = "Work", IsDefault = true, ColorHex = "#112233" };
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            ListId = list.Id,
            Title = "T \"q\" ç\nx",
            Notes = "n",
            Priority = Priority.Low,
            DueDate = new DateOnly(2026, 2, 28),
            DueTime = new TimeOnly(8, 45),
            ReminderUtc = new DateTime(2026, 2, 28, 7, 0, 0, 123, DateTimeKind.Utc),
            SnoozedUntilUtc = new DateTime(2026, 2, 28, 7, 30, 0, DateTimeKind.Utc),
            SeriesId = Guid.NewGuid(),
            NextOccurrenceId = Guid.NewGuid(),
            SortOrder = 1.0000001,
            Recurrence = Recurrence.Yearly(2, 29) with { Anchor = RecurrenceAnchor.CompletionDate, EndDate = new DateOnly(2040, 2, 29) },
            Tags = [tag],
            Subtasks = [new Subtask { Id = Guid.NewGuid(), Title = "s", IsCompleted = true, SortOrder = 0.5 }],
            CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        };

        var first = JsonExportFormat.Serialize(new ExportData([list], [tag], [task]), Exported, "1.0.0");
        var document = JsonExportFormat.Deserialize(first);
        var second = JsonExportFormat.Serialize(document);

        Assert.Equal(Encoding.UTF8.GetString(first), Encoding.UTF8.GetString(second));
        var read = Assert.Single(document.Tasks);
        Assert.Equal(task.ReminderUtc, read.ReminderUtc);
        Assert.Equal(task.SortOrder, read.SortOrder);
        Assert.Equal(new TimeOnly(8, 45), read.DueTime);
        Assert.Equal(RecurrenceAnchor.CompletionDate, read.Recurrence!.Anchor);
    }

    private static ExportData OneTask(TaskItem task) => new([], [], [task]);

    private static JsonElement FirstTask(JsonDocument json) => json.RootElement.GetProperty("tasks")[0];

    private static JsonDocument Parse(ExportData data) =>
        JsonDocument.Parse(JsonExportFormat.Serialize(data, Exported, "1.0.0"));
}
