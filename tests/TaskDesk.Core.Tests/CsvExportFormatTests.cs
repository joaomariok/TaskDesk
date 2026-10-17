using System.Text;
using TaskDesk.Core.Export;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public sealed class CsvExportFormatTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("plain", "plain")]
    [InlineData("with space", "with space")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("\"", "\"\"\"\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("line1\r\nline2", "\"line1\r\nline2\"")]
    [InlineData("old\rmac", "\"old\rmac\"")]
    [InlineData("a,\"b\"\nc", "\"a,\"\"b\"\"\nc\"")]
    [InlineData("ação ç ã é", "ação ç ã é")]
    [InlineData("São Paulo, açúcar", "\"São Paulo, açúcar\"")]
    [InlineData(" leading;semicolon ", " leading;semicolon ")]
    public void Escape_quotes_only_when_needed_and_doubles_inner_quotes(string? field, string expected)
    {
        Assert.Equal(expected, CsvExportFormat.Escape(field));
    }

    [Fact]
    public void The_header_lists_the_spec_columns_in_order()
    {
        var csv = CsvExportFormat.Build(Empty, TimeZoneInfo.Utc);

        Assert.Equal(
            "List,Title,Notes,Priority,DueDate,DueTime,Completed,CompletedLocal,Tags,Subtasks,Recurrence\r\n", csv);
    }

    [Fact]
    public void One_row_per_task_with_every_column_filled_from_the_task()
    {
        var work = new Guid("00000000-0000-0000-0000-0000000000a1");
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Work", NormalizedName = "WORK" };
        var other = new Tag { Id = Guid.NewGuid(), Name = "ação", NormalizedName = "AÇÃO" };
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            ListId = work,
            Title = "Pay \"Maria\", invoice",
            Notes = "first line\nsecond line",
            Priority = Priority.High,
            DueDate = new DateOnly(2026, 10, 20),
            DueTime = new TimeOnly(14, 0),
            IsCompleted = true,
            CompletedUtc = new DateTime(2026, 10, 20, 2, 5, 0, DateTimeKind.Utc),
            Recurrence = Recurrence.Weekly(RecurrenceDays.Monday | RecurrenceDays.Thursday, interval: 2),
            Tags = [other, tag],
            Subtasks =
            [
                new Subtask { Id = Guid.NewGuid(), Title = "second", SortOrder = 2 },
                new Subtask { Id = Guid.NewGuid(), Title = "first, done", IsCompleted = true, SortOrder = 1 },
            ],
        };
        var data = new ExportData(
            [new TaskList { Id = work, Name = "Work" }], [tag, other], [task]);

        var csv = CsvExportFormat.Build(data, TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(-3), "t", "t"));

        Assert.Equal(
            "List,Title,Notes,Priority,DueDate,DueTime,Completed,CompletedLocal,Tags,Subtasks,Recurrence\r\n"
            + "Work,\"Pay \"\"Maria\"\", invoice\",\"first line\nsecond line\",High,2026-10-20,14:00,true,"
            + "2026-10-19 23:05,ação Work,\"[x] first, done; second\",\"Every 2 weeks on Mon, Thu\"\r\n",
            csv);
    }

    [Fact]
    public void A_task_without_optional_values_has_empty_cells()
    {
        var list = new TaskList { Id = Guid.NewGuid(), Name = "Inbox" };
        var task = new TaskItem { Id = Guid.NewGuid(), ListId = list.Id, Title = "Plain" };

        var csv = CsvExportFormat.Build(new ExportData([list], [], [task]), TimeZoneInfo.Utc);

        Assert.EndsWith("\r\nInbox,Plain,,None,,,false,,,,\r\n", csv);
    }

    [Fact]
    public void Tasks_follow_the_list_order_then_the_manual_order()
    {
        var first = new TaskList { Id = Guid.NewGuid(), Name = "B first", SortOrder = 0 };
        var second = new TaskList { Id = Guid.NewGuid(), Name = "A second", SortOrder = 1 };
        var data = new ExportData(
            [second, first],
            [],
            [
                new TaskItem { Id = Guid.NewGuid(), ListId = second.Id, Title = "s1", SortOrder = 1 },
                new TaskItem { Id = Guid.NewGuid(), ListId = first.Id, Title = "f2", SortOrder = 2 },
                new TaskItem { Id = Guid.NewGuid(), ListId = first.Id, Title = "f1", SortOrder = 1 },
            ]);

        var titles = CsvExportFormat.Build(data, TimeZoneInfo.Utc).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Skip(1).Select(row => row.Split(',')[1]);

        Assert.Equal(["f1", "f2", "s1"], titles);
    }

    [Fact]
    public void The_file_starts_with_a_utf8_bom_and_keeps_accents_as_utf8()
    {
        var list = new TaskList { Id = Guid.NewGuid(), Name = "Ação" };
        var task = new TaskItem { Id = Guid.NewGuid(), ListId = list.Id, Title = "Coração, não" };

        var bytes = CsvExportFormat.Serialize(new ExportData([list], [], [task]), TimeZoneInfo.Utc);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal((byte)'L', bytes[3]); // the header's "List" right after the BOM: written exactly once
        var text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        Assert.Contains("Ação,\"Coração, não\"", text);
    }

    private static ExportData Empty => new([], [], []);
}
