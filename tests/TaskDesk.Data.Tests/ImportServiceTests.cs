using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Export;
using TaskDesk.Core.Import;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public sealed class ImportServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid WorkListId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkTagId = new("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid UnusedTagId = new("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid RecurringId = new("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid DoneId = new("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid NextId = new("00000000-0000-0000-0000-0000000000b3");
    private static readonly Guid BirthdayId = new("00000000-0000-0000-0000-0000000000b4");
    private static readonly Guid SeriesId = new("00000000-0000-0000-0000-0000000000c1");

    /// <summary>A database with two Lists, two Tags and a Series of two Occurrences, one with a dangling link.</summary>
    private static async Task<TempDatabase> SeedSourceAsync()
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
        var done = new TaskItem
        {
            Id = DoneId,
            ListId = WorkListId,
            Title = "Pay \"Maria\", invoice",
            Notes = "line one\nline two ção",
            Priority = Priority.High,
            IsCompleted = true,
            CompletedUtc = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc),
            DueDate = new DateOnly(2026, 10, 13),
            DueTime = new TimeOnly(14, 0),
            ReminderUtc = new DateTime(2026, 10, 13, 13, 0, 0, DateTimeKind.Utc),
            SortOrder = 1.5,
            SeriesId = SeriesId,
            NextOccurrenceId = RecurringId,
            Recurrence = Recurrence.Weekly(RecurrenceDays.Tuesday) with { EndDate = new DateOnly(2027, 1, 31) },
            Tags = [tag],
            Subtasks = [new Subtask { Id = Guid.CreateVersion7(), Title = "draft", IsCompleted = true, SortOrder = 1 }],
            CreatedUtc = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc),
        };
        var recurring = new TaskItem
        {
            Id = RecurringId,
            ListId = WorkListId,
            Title = "Pay \"Maria\", invoice",
            Priority = Priority.High,
            DueDate = new DateOnly(2026, 10, 20),
            DueTime = new TimeOnly(14, 0),
            ReminderUtc = new DateTime(2026, 10, 20, 13, 0, 0, DateTimeKind.Utc),
            SnoozedUntilUtc = new DateTime(2026, 10, 20, 13, 15, 0, DateTimeKind.Utc),
            SortOrder = 2.5,
            SeriesId = SeriesId,
            NextOccurrenceId = NextId, // the Occurrence it produced was deleted later: a dangling link (ADR 0001)
            Recurrence = Recurrence.Weekly(RecurrenceDays.Tuesday) with { EndDate = new DateOnly(2027, 1, 31) },
            Tags = [tag],
            Subtasks =
            [
                new Subtask { Id = Guid.CreateVersion7(), Title = "draft", SortOrder = 1 },
                new Subtask { Id = Guid.CreateVersion7(), Title = "send, then file", SortOrder = 2 },
            ],
            CreatedUtc = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc),
        };
        var birthday = new TaskItem
        {
            Id = BirthdayId,
            ListId = inbox.Id,
            Title = "Birthday",
            DueDate = new DateOnly(2027, 3, 14),
            ReminderUtc = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc), // already past, never fired
            SortOrder = 1,
            Recurrence = Recurrence.Yearly(3, 14) with { Anchor = RecurrenceAnchor.CompletionDate },
            CreatedUtc = new DateTime(2026, 10, 2, 9, 5, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 10, 2, 9, 5, 0, DateTimeKind.Utc),
        };

        context.AddRange(work, tag, unused, done, recurring, birthday);
        await context.SaveChangesAsync(Ct);
        return db;
    }

    private static async Task<TempDatabase> FreshAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return db;
    }

    private static async Task<byte[]> ExportAsync(TempDatabase db)
    {
        var path = Path.Combine(db.Paths.DataDirectory, $"export-{Guid.NewGuid():N}.json");
        await db.Get<IExportService>().ExportJsonAsync(path, "1.0.0", Ct);
        return await File.ReadAllBytesAsync(path, Ct);
    }

    private static async Task<string> WriteFileAsync(TempDatabase db, byte[] bytes)
    {
        var path = Path.Combine(db.Paths.DataDirectory, $"import-{Guid.NewGuid():N}.json");
        await File.WriteAllBytesAsync(path, bytes, Ct);
        return path;
    }

    [Fact]
    public async Task Export_then_replace_into_a_fresh_database_then_export_again_gives_identical_bytes()
    {
        using var source = await SeedSourceAsync();
        using var target = await FreshAsync();
        var original = await ExportAsync(source);

        await target.Get<IImportService>().ImportJsonAsync(await WriteFileAsync(target, original), ImportMode.Replace, Ct);

        Assert.Equal(original, await ExportAsync(target));
    }
    [Fact]
    public async Task Replace_backs_up_first_deletes_local_data_and_makes_the_files_default_list_the_default()
    {
        using var source = await SeedSourceAsync();
        using var target = await FreshAsync();
        var localInbox = (await target.QueryAsync(c => c.Lists)).Single();
        await target.AddTaskAsync(localInbox.Id, "local only", completed: false);
        var sourceInbox = (await source.QueryAsync(c => c.Lists.Where(l => l.IsDefault))).Single();
        var file = await WriteFileAsync(target, await ExportAsync(source));

        var result = await target.Get<IImportService>().ImportJsonAsync(file, ImportMode.Replace, Ct);

        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal(new ImportCounts(2, 0), result.Lists);
        Assert.Equal(new ImportCounts(2, 0), result.Tags);
        Assert.Equal(new ImportCounts(3, 0), result.Tasks);
        var lists = await target.QueryAsync(c => c.Lists);
        Assert.DoesNotContain(lists, l => l.Id == localInbox.Id);
        Assert.Equal(sourceInbox.Id, Assert.Single(lists, l => l.IsDefault).Id);
        Assert.DoesNotContain(await target.QueryAsync(c => c.Tasks), t => t.Title == "local only");
    }

    [Fact]
    public async Task Series_and_occurrence_links_are_kept_including_a_dangling_next_occurrence()
    {
        using var source = await SeedSourceAsync();
        using var target = await FreshAsync();

        await target.Get<IImportService>().ImportJsonAsync(
            await WriteFileAsync(target, await ExportAsync(source)), ImportMode.Replace, Ct);

        var tasks = await target.QueryAsync(c => c.Tasks.Where(t => t.SeriesId == SeriesId));
        Assert.Equal(RecurringId, tasks.Single(t => t.Id == DoneId).NextOccurrenceId);
        Assert.Equal(NextId, tasks.Single(t => t.Id == RecurringId).NextOccurrenceId);
    }

    [Fact]
    public async Task A_reminder_whose_time_has_passed_is_imported_as_already_fired_and_a_future_one_stays_armed()
    {
        using var source = await SeedSourceAsync();
        using var target = await FreshAsync();

        await target.Get<IImportService>().ImportJsonAsync(
            await WriteFileAsync(target, await ExportAsync(source)), ImportMode.Replace, Ct);

        var tasks = await target.QueryAsync(c => c.Tasks);
        var importedAt = target.Clock.GetUtcNow().UtcDateTime;
        Assert.Equal(importedAt, tasks.Single(t => t.Id == BirthdayId).NotifiedThroughUtc);
        Assert.Equal(importedAt, tasks.Single(t => t.Id == RecurringId).NotifiedThroughUtc);
        var reminders = target.Get<TaskDesk.Core.Reminders.IReminderService>();
        Assert.Empty(await reminders.ListDueAsync(Ct));

        // The future one fires when its Snooze ends (20 Oct 13:15 UTC).
        target.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 20, 13, 15, 0, TimeSpan.Zero));
        var due = Assert.Single(await reminders.ListDueAsync(Ct));
        Assert.Equal(RecurringId, due.TaskId);
    }
    /// <summary>A target whose Default List is "Caixa" and which has its own "WORK" List, "Work" Tag and a Task with DoneId.</summary>
    private static async Task<(TempDatabase Db, Guid CaixaId, Guid LocalWorkId, Guid LocalTagId)> SeedTargetAsync()
    {
        var db = await FreshAsync();
        await db.RenameDefaultListAsync("Caixa");
        await using var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(Ct);
        var caixa = await context.Lists.SingleAsync(l => l.IsDefault, Ct);
        var localWork = new TaskList { Id = Guid.CreateVersion7(), Name = "WORK", SortOrder = 1, CreatedUtc = caixa.CreatedUtc };
        var localTag = new Tag { Id = Guid.CreateVersion7(), Name = "Work", NormalizedName = "WORK" };
        var clash = new TaskItem
        {
            Id = DoneId,
            ListId = caixa.Id,
            Title = "local version",
            SortOrder = 1,
            CreatedUtc = caixa.CreatedUtc,
            UpdatedUtc = caixa.CreatedUtc,
        };
        context.AddRange(localWork, localTag, clash);
        await context.SaveChangesAsync(Ct);
        return (db, caixa.Id, localWork.Id, localTag.Id);
    }

    [Fact]
    public async Task Merge_adds_new_ids_skips_existing_ones_and_matches_lists_and_tags_by_name()
    {
        using var source = await SeedSourceAsync();
        var (target, caixaId, localWorkId, localTagId) = await SeedTargetAsync();
        using (target)
        {
            var sourceInbox = (await source.QueryAsync(c => c.Lists.Where(l => l.IsDefault))).Single();
            var file = await WriteFileAsync(target, await ExportAsync(source));

            var result = await target.Get<IImportService>().ImportJsonAsync(file, ImportMode.Merge, Ct);

            Assert.Null(result.BackupPath);
            Assert.Equal(new ImportCounts(1, 1), result.Lists);
            Assert.Equal(new ImportCounts(1, 1), result.Tags);
            Assert.Equal(new ImportCounts(2, 1), result.Tasks);

            // The file's default "Inbox" matches nothing here, so it arrives as an ordinary List after the local ones.
            var lists = await target.QueryAsync(c => c.Lists);
            Assert.Equal(caixaId, Assert.Single(lists, l => l.IsDefault).Id);
            var inbox = Assert.Single(lists, l => l.Id == sourceInbox.Id);
            Assert.False(inbox.IsDefault);
            Assert.Equal("Inbox", inbox.Name);
            Assert.True(inbox.SortOrder > lists.Where(l => l.Id != inbox.Id).Max(l => l.SortOrder));
            Assert.DoesNotContain(lists, l => l.Id == WorkListId);

            var tags = await target.QueryAsync(c => c.Tags);
            Assert.Equal(["IDEIAS", "WORK"], tags.Select(t => t.NormalizedName).Order());
            Assert.Equal(UnusedTagId, tags.Single(t => t.NormalizedName == "IDEIAS").Id);

            var tasks = await target.QueryAsync(c => c.Tasks.Include(t => t.Tags).Include(t => t.Subtasks));
            Assert.Equal("local version", tasks.Single(t => t.Id == DoneId).Title);
            var recurring = tasks.Single(t => t.Id == RecurringId);
            Assert.Equal(localWorkId, recurring.ListId);
            Assert.Equal(localTagId, Assert.Single(recurring.Tags).Id);
            Assert.Equal(2, recurring.Subtasks.Count);
            Assert.Equal(SeriesId, recurring.SeriesId);
            Assert.Equal(NextId, recurring.NextOccurrenceId);
            Assert.Equal(sourceInbox.Id, tasks.Single(t => t.Id == BirthdayId).ListId);
        }
    }

    [Fact]
    public async Task Merging_a_databases_own_export_back_adds_nothing()
    {
        using var source = await SeedSourceAsync();
        var before = await ExportAsync(source);

        var result = await source.Get<IImportService>().ImportJsonAsync(
            await WriteFileAsync(source, before), ImportMode.Merge, Ct);

        Assert.Equal(new ImportCounts(0, 2), result.Lists);
        Assert.Equal(new ImportCounts(0, 2), result.Tags);
        Assert.Equal(new ImportCounts(0, 3), result.Tasks);
        Assert.Equal(before, await ExportAsync(source));
    }
    private static int BackupCount(TempDatabase db) =>
        Directory.Exists(db.Paths.BackupsDirectory) ? Directory.GetFiles(db.Paths.BackupsDirectory).Length : 0;

    public static TheoryData<string, ImportMode> RejectedFiles() => new()
    {
        { """{ "format": "something-else", "schemaVersion": 1, "lists": [], "tags": [], "tasks": [] }""", ImportMode.Replace },
        { """{ "format": "taskdesk-export", "schemaVersion": 2, "lists": [], "tags": [], "tasks": [] }""", ImportMode.Replace },
        { "{ \"format\": \"taskdesk-export\", \"schemaVersion\": 1, \"lists\": [", ImportMode.Replace },
        { "dangling list", ImportMode.Replace },
        { "dangling list", ImportMode.Merge },
        { "invalid title", ImportMode.Replace },
        { "invalid title", ImportMode.Merge },
    };

    [Theory]
    [MemberData(nameof(RejectedFiles))]
    public async Task A_rejected_file_leaves_the_database_unchanged_and_takes_no_backup(string content, ImportMode mode)
    {
        using var db = await SeedSourceAsync();
        var before = await ExportAsync(db);
        var bytes = content switch
        {
            "dangling list" => Mutate(before, d => d with { Tasks = [d.Tasks[0] with { ListId = Guid.NewGuid() }] }),
            "invalid title" => Mutate(before, d => d with { Tasks = [.. d.Tasks.Select(t => t with { Title = "" })] }),
            _ => System.Text.Encoding.UTF8.GetBytes(content),
        };
        var file = await WriteFileAsync(db, bytes);
        var backups = BackupCount(db);

        await Assert.ThrowsAsync<ImportException>(() => db.Get<IImportService>().ImportJsonAsync(file, mode, Ct));

        Assert.Equal(backups, BackupCount(db));
        Assert.Equal(before, await ExportAsync(db));
    }

    private static byte[] Mutate(byte[] export, Func<ExportDocument, ExportDocument> change) =>
        JsonExportFormat.Serialize(change(JsonExportFormat.Deserialize(export)));

    /// <summary>Makes SQLite itself refuse one row, so a write fails halfway through the import transaction.</summary>
    private static async Task FailInsertsOfTheSendSubtaskAsync(TempDatabase db)
    {
        await using var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(Ct);
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_import BEFORE INSERT ON Subtasks WHEN NEW.Title = 'send, then file' "
            + "BEGIN SELECT RAISE(ABORT, 'injected failure'); END;",
            Ct);
    }

    [Theory]
    [InlineData(ImportMode.Replace)]
    [InlineData(ImportMode.Merge)]
    public async Task A_failure_halfway_through_rolls_the_whole_import_back(ImportMode mode)
    {
        using var source = await SeedSourceAsync();
        var (target, _, _, _) = await SeedTargetAsync();
        using (target)
        {
            await FailInsertsOfTheSendSubtaskAsync(target);
            var before = await ExportAsync(target);
            var file = await WriteFileAsync(target, await ExportAsync(source));

            await Assert.ThrowsAnyAsync<DbUpdateException>(() => target.Get<IImportService>().ImportJsonAsync(file, mode, Ct));

            Assert.Equal(before, await ExportAsync(target));
        }
    }
}
