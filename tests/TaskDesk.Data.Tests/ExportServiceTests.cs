using System.Text;
using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Export;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public sealed class ExportServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid WorkListId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkTagId = new("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid UnusedTagId = new("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid RecurringId = new("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid DoneId = new("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid SeriesId = new("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid NextId = new("00000000-0000-0000-0000-0000000000b3");
    private static readonly Guid SubtaskOneId = new("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid SubtaskTwoId = new("00000000-0000-0000-0000-0000000000d2");

    private static async Task<(TempDatabase Db, Guid InboxId, DateTime InboxCreated)> SeedAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(Ct);
        var inbox = await context.Lists.SingleAsync(l => l.IsDefault, Ct);

        var work = new TaskList
        {
            Id = WorkListId,
            Name = "Work",
            ColorHex = "#3366FF",
            SortOrder = 1,
            CreatedUtc = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
        };
        var tag = new Tag { Id = WorkTagId, Name = "work", NormalizedName = "WORK", ColorHex = "#3366FF" };
        var unused = new Tag { Id = UnusedTagId, Name = "ideias", NormalizedName = "IDEIAS" };

        var recurring = new TaskItem
        {
            Id = RecurringId,
            ListId = WorkListId,
            Title = "Pay \"Maria\", invoice",
            Notes = "line one\nline two ção",
            Priority = Priority.High,
            DueDate = new DateOnly(2026, 10, 20),
            DueTime = new TimeOnly(14, 0),
            ReminderUtc = new DateTime(2026, 10, 20, 13, 0, 0, DateTimeKind.Utc),
            SnoozedUntilUtc = new DateTime(2026, 10, 20, 13, 15, 0, DateTimeKind.Utc),
            NotifiedThroughUtc = new DateTime(2026, 10, 20, 13, 0, 1, DateTimeKind.Utc),
            SortOrder = 1.5,
            SeriesId = SeriesId,
            NextOccurrenceId = NextId,
            Recurrence = Recurrence.Weekly(RecurrenceDays.Monday | RecurrenceDays.Thursday) with
            {
                EndDate = new DateOnly(2027, 1, 31),
            },
            Tags = [tag],
            Subtasks =
            [
                new Subtask { Id = SubtaskOneId, Title = "draft", IsCompleted = true, SortOrder = 1 },
                new Subtask { Id = SubtaskTwoId, Title = "send, then file", SortOrder = 2 },
            ],
            CreatedUtc = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc),
        };
        var done = new TaskItem
        {
            Id = DoneId,
            ListId = inbox.Id,
            Title = "Birthday",
            Priority = Priority.None,
            IsCompleted = true,
            CompletedUtc = new DateTime(2026, 10, 16, 2, 30, 0, DateTimeKind.Utc),
            DueDate = new DateOnly(2026, 3, 14),
            SortOrder = 2.25,
            Recurrence = Recurrence.Yearly(3, 14) with { Anchor = RecurrenceAnchor.CompletionDate },
            CreatedUtc = new DateTime(2026, 10, 2, 9, 5, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 10, 16, 2, 30, 0, DateTimeKind.Utc),
        };

        context.AddRange(work, tag, unused, recurring, done);
        await context.SaveChangesAsync(Ct);
        return (db, inbox.Id, inbox.CreatedUtc);
    }

    [Fact]
    public async Task The_json_export_of_a_database_with_every_kind_of_field_matches_the_expected_document()
    {
        var (db, inboxId, inboxCreated) = await SeedAsync();
        using (db)
        {
            var path = Path.Combine(db.Paths.DataDirectory, "export.json");

            await db.Get<IExportService>().ExportJsonAsync(path, "1.0.0", Ct);

            var bytes = await File.ReadAllBytesAsync(path, Ct);
            Assert.NotEqual(0xEF, bytes[0]); // no BOM
            var created = inboxCreated.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'");
            var expected = $$"""
                {
                  "format": "taskdesk-export",
                  "schemaVersion": 1,
                  "exportedUtc": "2026-10-16T09:30:00Z",
                  "appVersion": "1.0.0",
                  "lists": [
                    {
                      "id": "{{inboxId}}",
                      "name": "Inbox",
                      "colorHex": null,
                      "sortOrder": 0,
                      "isDefault": true,
                      "createdUtc": "{{created}}"
                    },
                    {
                      "id": "00000000-0000-0000-0000-000000000001",
                      "name": "Work",
                      "colorHex": "#3366FF",
                      "sortOrder": 1,
                      "isDefault": false,
                      "createdUtc": "2026-10-01T08:00:00Z"
                    }
                  ],
                  "tags": [
                    {
                      "id": "00000000-0000-0000-0000-0000000000a2",
                      "name": "ideias",
                      "colorHex": null
                    },
                    {
                      "id": "00000000-0000-0000-0000-0000000000a1",
                      "name": "work",
                      "colorHex": "#3366FF"
                    }
                  ],
                  "tasks": [
                    {
                      "id": "00000000-0000-0000-0000-0000000000b2",
                      "listId": "{{inboxId}}",
                      "title": "Birthday",
                      "notes": null,
                      "priority": "None",
                      "dueDate": "2026-03-14",
                      "dueTime": null,
                      "reminderUtc": null,
                      "snoozedUntilUtc": null,
                      "isCompleted": true,
                      "completedUtc": "2026-10-16T02:30:00Z",
                      "sortOrder": 2.25,
                      "seriesId": null,
                      "nextOccurrenceId": null,
                      "recurrence": {
                        "frequency": "Yearly",
                        "interval": 1,
                        "daysOfWeek": [],
                        "month": 3,
                        "dayOfMonth": 14,
                        "anchor": "CompletionDate",
                        "endDate": null
                      },
                      "tagIds": [],
                      "subtasks": [],
                      "createdUtc": "2026-10-02T09:05:00Z",
                      "updatedUtc": "2026-10-16T02:30:00Z"
                    },
                    {
                      "id": "00000000-0000-0000-0000-0000000000b1",
                      "listId": "00000000-0000-0000-0000-000000000001",
                      "title": "Pay \"Maria\", invoice",
                      "notes": "line one\nline two ção",
                      "priority": "High",
                      "dueDate": "2026-10-20",
                      "dueTime": "14:00",
                      "reminderUtc": "2026-10-20T13:00:00Z",
                      "snoozedUntilUtc": "2026-10-20T13:15:00Z",
                      "isCompleted": false,
                      "completedUtc": null,
                      "sortOrder": 1.5,
                      "seriesId": "00000000-0000-0000-0000-0000000000c1",
                      "nextOccurrenceId": "00000000-0000-0000-0000-0000000000b3",
                      "recurrence": {
                        "frequency": "Weekly",
                        "interval": 1,
                        "daysOfWeek": [
                          "Monday",
                          "Thursday"
                        ],
                        "month": null,
                        "dayOfMonth": null,
                        "anchor": "DueDate",
                        "endDate": "2027-01-31"
                      },
                      "tagIds": [
                        "00000000-0000-0000-0000-0000000000a1"
                      ],
                      "subtasks": [
                        {
                          "id": "00000000-0000-0000-0000-0000000000d1",
                          "title": "draft",
                          "isCompleted": true,
                          "sortOrder": 1
                        },
                        {
                          "id": "00000000-0000-0000-0000-0000000000d2",
                          "title": "send, then file",
                          "isCompleted": false,
                          "sortOrder": 2
                        }
                      ],
                      "createdUtc": "2026-10-02T09:00:00Z",
                      "updatedUtc": "2026-10-03T10:00:00Z"
                    }
                  ]
                }
                """;
            Assert.Equal(expected, Encoding.UTF8.GetString(bytes));
        }
    }

    [Fact]
    public async Task The_json_export_round_trips_through_the_reader_without_loss()
    {
        var (db, _, _) = await SeedAsync();
        using (db)
        {
            var path = Path.Combine(db.Paths.DataDirectory, "export.json");
            await db.Get<IExportService>().ExportJsonAsync(path, "1.0.0", Ct);
            var bytes = await File.ReadAllBytesAsync(path, Ct);

            var document = JsonExportFormat.Deserialize(bytes);

            Assert.Equal(bytes, JsonExportFormat.Serialize(document));
            Assert.Equal(2, document.Tasks.Count);
            Assert.Equal(2, document.Lists.Count);
            Assert.Single(document.Lists, l => l.IsDefault);
        }
    }

    [Fact]
    public async Task The_csv_export_has_a_bom_and_one_row_per_task()
    {
        var (db, _, _) = await SeedAsync();
        using (db)
        {
            var path = Path.Combine(db.Paths.DataDirectory, "export.csv");

            await db.Get<IExportService>().ExportCsvAsync(path, Ct);

            var bytes = await File.ReadAllBytesAsync(path, Ct);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            var text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            Assert.StartsWith("List,Title,Notes,Priority,DueDate,DueTime,Completed,CompletedLocal,Tags,Subtasks,Recurrence\r\n", text);
            Assert.Contains(
                "Work,\"Pay \"\"Maria\"\", invoice\",\"line one\nline two ção\",High,2026-10-20,14:00,false,,work,"
                + "\"[x] draft; send, then file\",\"Every week on Mon, Thu, until 2027-01-31\"\r\n",
                text);
            Assert.Contains("Inbox,Birthday,,None,2026-03-14,,true,", text);
            Assert.Contains(",\"Every year on Mar 14, from completion date\"\r\n", text);
        }
    }

    [Fact]
    public async Task Exporting_replaces_an_existing_file_and_leaves_no_temp_file()
    {
        var (db, _, _) = await SeedAsync();
        using (db)
        {
            var path = Path.Combine(db.Paths.DataDirectory, "export.json");
            await File.WriteAllTextAsync(path, "old", Ct);

            await db.Get<IExportService>().ExportJsonAsync(path, "1.0.0", Ct);

            Assert.StartsWith("{", await File.ReadAllTextAsync(path, Ct));
            Assert.False(File.Exists(path + ".tmp"));
        }
    }

    [Fact]
    public async Task A_failed_write_throws_and_leaves_no_temp_file()
    {
        var (db, _, _) = await SeedAsync();
        using (db)
        {
            var path = Path.Combine(db.Paths.DataDirectory, "missing-folder", "export.json");

            await Assert.ThrowsAnyAsync<IOException>(() => db.Get<IExportService>().ExportJsonAsync(path, "1.0.0", Ct));

            Assert.False(File.Exists(path + ".tmp"));
        }
    }
}
