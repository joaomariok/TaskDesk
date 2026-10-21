using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;

namespace TaskDesk.Data.Tests;

public class DatabaseInitializerTests
{
    [Fact]
    public async Task First_run_creates_the_database_file_and_seeds_one_Default_List_named_Inbox()
    {
        using var db = new TempDatabase();

        await db.Get<IDatabaseInitializer>().InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(File.Exists(db.Paths.DatabaseFile));
        var lists = await db.QueryAsync(c => c.Lists);
        var list = Assert.Single(lists);
        Assert.Equal("Inbox", list.Name);
        Assert.True(list.IsDefault);
    }

    [Fact]
    public async Task Initializing_again_does_not_seed_a_second_list()
    {
        using var db = new TempDatabase();
        var initializer = db.Get<IDatabaseInitializer>();

        await initializer.InitializeAsync(TestContext.Current.CancellationToken);
        await initializer.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Single(await db.QueryAsync(c => c.Lists));
    }

    [Fact]
    public async Task A_renamed_Default_List_is_kept_and_not_seeded_again()
    {
        using var db = new TempDatabase();
        var initializer = db.Get<IDatabaseInitializer>();
        await initializer.InitializeAsync(TestContext.Current.CancellationToken);
        await db.RenameDefaultListAsync("Work");

        await initializer.InitializeAsync(TestContext.Current.CancellationToken);

        var list = Assert.Single(await db.QueryAsync(c => c.Lists));
        Assert.Equal("Work", list.Name);
    }

    // ---- Ticket 115: the fired marker becomes "notified through" ----

    private static string Sql(DateTime? utc) => utc?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";

    private static async Task InsertReminderTaskAsync(
        TaskDeskDbContext context, Guid listId, string title, DateTime? reminder, DateTime? snoozed, DateTime? fired,
        bool completed = false)
    {
        var created = Sql(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO Tasks (Id, ListId, Title, Priority, IsCompleted, SortOrder, CreatedUtc, UpdatedUtc,
                ReminderUtc, SnoozedUntilUtc, ReminderFiredUtc)
            VALUES ({Guid.NewGuid().ToString().ToUpperInvariant()}, {listId.ToString().ToUpperInvariant()}, {title},
                0, {(completed ? 1 : 0)}, 1, {created}, {created},
                NULLIF({Sql(reminder)}, ''), NULLIF({Sql(snoozed)}, ''), NULLIF({Sql(fired)}, ''))
            """,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Upgrading_keeps_fired_Reminders_fired_armed_ones_armed_and_running_Snoozes_running()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = new TempDatabase();
        db.Paths.EnsureDirectories();
        var now = db.Clock.GetUtcNow().UtcDateTime;
        await using (var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(ct))
        {
            await context.Database.GetService<IMigrator>().MigrateAsync("20261016233045_Ticket13_Reminders", ct);
            var listId = Guid.NewGuid();
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO Lists (Id, Name, SortOrder, IsDefault, CreatedUtc)
                VALUES ({listId.ToString().ToUpperInvariant()}, 'Inbox', 0, 1, {Sql(now)})
                """,
                ct);
            await InsertReminderTaskAsync(context, listId, "fired", now.AddHours(-1), null, now.AddMinutes(-59));
            await InsertReminderTaskAsync(context, listId, "armed past", now.AddHours(-1), null, null);
            await InsertReminderTaskAsync(context, listId, "armed future", now.AddHours(1), null, null);
            await InsertReminderTaskAsync(context, listId, "snoozed", now.AddHours(-2), now.AddMinutes(30), null);
            await InsertReminderTaskAsync(context, listId, "snooze fired", now.AddHours(-3), now.AddMinutes(-20), now.AddMinutes(-19));
            await InsertReminderTaskAsync(context, listId, "completed", now.AddHours(-1), null, null, completed: true);
            await InsertReminderTaskAsync(context, listId, "no reminder", null, null, null);
        }

        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var reminders = db.Get<IReminderService>();

        var due = Assert.Single(await reminders.ListDueAsync(ct));
        Assert.Equal(("armed past", now.AddHours(-1)), (due.Title, due.DueAtUtc));
        Assert.True(await reminders.TryMarkFiredAsync(due.TaskId, due.DueAtUtc, ct));
        Assert.Empty(await reminders.ListDueAsync(ct));

        db.Clock.Advance(TimeSpan.FromMinutes(30));
        due = Assert.Single(await reminders.ListDueAsync(ct));
        Assert.Equal(("snoozed", now.AddMinutes(30)), (due.Title, due.DueAtUtc));
        Assert.True(await reminders.TryMarkFiredAsync(due.TaskId, due.DueAtUtc, ct));

        db.Clock.Advance(TimeSpan.FromMinutes(30));
        due = Assert.Single(await reminders.ListDueAsync(ct));
        Assert.Equal(("armed future", now.AddHours(1)), (due.Title, due.DueAtUtc));
    }

    // ---- Ticket 116: each Reminder becomes a row owned by its Task ----

    private static async Task<Guid> InsertNotifiedTaskAsync(
        TaskDeskDbContext context, Guid listId, string title, DateTime? reminder, DateTime? notifiedThrough)
    {
        var id = Guid.NewGuid();
        var created = Sql(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO Tasks (Id, ListId, Title, Priority, IsCompleted, SortOrder, CreatedUtc, UpdatedUtc,
                ReminderUtc, NotifiedThroughUtc)
            VALUES ({id.ToString().ToUpperInvariant()}, {listId.ToString().ToUpperInvariant()}, {title},
                0, 0, 1, {created}, {created}, NULLIF({Sql(reminder)}, ''), NULLIF({Sql(notifiedThrough)}, ''))
            """,
            TestContext.Current.CancellationToken);
        return id;
    }

    [Fact]
    public async Task Upgrading_turns_each_Reminder_into_one_Fixed_Reminder_and_changes_nothing_about_what_fires()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = new TempDatabase();
        db.Paths.EnsureDirectories();
        var now = db.Clock.GetUtcNow().UtcDateTime;
        Guid fired, armedPast, armedFuture, plain;
        await using (var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(ct))
        {
            await context.Database.GetService<IMigrator>().MigrateAsync("20261020230021_Ticket115_NotifiedThrough", ct);
            var listId = Guid.NewGuid();
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO Lists (Id, Name, SortOrder, IsDefault, CreatedUtc)
                VALUES ({listId.ToString().ToUpperInvariant()}, 'Inbox', 0, 1, {Sql(now)})
                """,
                ct);
            fired = await InsertNotifiedTaskAsync(context, listId, "fired", now.AddHours(-1), now.AddHours(-1));
            armedPast = await InsertNotifiedTaskAsync(context, listId, "armed past", now.AddHours(-2), now.AddHours(-3));
            armedFuture = await InsertNotifiedTaskAsync(context, listId, "armed future", now.AddHours(1), now.AddHours(-3));
            plain = await InsertNotifiedTaskAsync(context, listId, "no reminder", null, null);
        }

        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);

        var tasks = (await db.Get<TaskDesk.Core.Tasks.ITaskService>().ListDefaultListTasksAsync(ct)).ToDictionary(t => t.Id);
        Assert.Equal(
            [(ReminderKind.Fixed, (DateTime?)now.AddHours(-1))],
            tasks[fired].Reminders.Select(r => (r.Kind, r.AtUtc)));
        Assert.Equal([(DateTime?)now.AddHours(-2)], tasks[armedPast].Reminders.Select(r => r.AtUtc));
        Assert.Equal([(DateTime?)now.AddHours(1)], tasks[armedFuture].Reminders.Select(r => r.AtUtc));
        Assert.Empty(tasks[plain].Reminders);
        Assert.Equal(3, (await db.QueryAsync(c => c.Reminders)).Count);

        var reminders = db.Get<IReminderService>();
        var due = Assert.Single(await reminders.ListDueAsync(ct));
        Assert.Equal((armedPast, now.AddHours(-2)), (due.TaskId, due.DueAtUtc));
        Assert.True(await reminders.TryMarkFiredAsync(due.TaskId, due.DueAtUtc, ct));

        db.Clock.Advance(TimeSpan.FromHours(1));
        due = Assert.Single(await reminders.ListDueAsync(ct));
        Assert.Equal((armedFuture, now.AddHours(1)), (due.TaskId, due.DueAtUtc));
    }

    // ---- Ticket 118: the Due notification ----

    private static async Task<Guid> InsertDueTaskAsync(
        TaskDeskDbContext context, Guid listId, string title, string dueDate, string? dueTime,
        DateTime? notifiedThrough = null, DateTime? snoozed = null, DateTime? reminder = null)
    {
        var id = Guid.NewGuid();
        var created = Sql(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO Tasks (Id, ListId, Title, Priority, IsCompleted, SortOrder, CreatedUtc, UpdatedUtc,
                DueDate, DueTime, NotifiedThroughUtc, SnoozedUntilUtc)
            VALUES ({id.ToString().ToUpperInvariant()}, {listId.ToString().ToUpperInvariant()}, {title},
                0, 0, 1, {created}, {created}, {dueDate}, NULLIF({dueTime ?? ""}, ''),
                NULLIF({Sql(notifiedThrough)}, ''), NULLIF({Sql(snoozed)}, ''))
            """,
            TestContext.Current.CancellationToken);
        if (reminder is not null)
        {
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO Reminders (Id, TaskId, Kind, AtUtc)
                VALUES ({Guid.NewGuid().ToString().ToUpperInvariant()}, {id.ToString().ToUpperInvariant()}, 0,
                    {Sql(reminder)})
                """,
                TestContext.Current.CancellationToken);
        }

        return id;
    }

    [Fact]
    public async Task Upgrading_turns_Notify_at_Due_on_and_counts_Dues_already_past_as_notified()
    {
        // Now is 2026-10-16 09:30 UTC; the time zone is UTC and the default Reminder time 09:00.
        var ct = TestContext.Current.CancellationToken;
        using var db = new TempDatabase();
        db.Paths.EnsureDirectories();
        var now = db.Clock.GetUtcNow().UtcDateTime;
        Guid pastDue, dueAhead, armedPast, snoozed;
        await using (var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(ct))
        {
            await context.Database.GetService<IMigrator>().MigrateAsync("20261020235855_Ticket117_BeforeDue", ct);
            var listId = Guid.NewGuid();
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO Lists (Id, Name, SortOrder, IsDefault, CreatedUtc)
                VALUES ({listId.ToString().ToUpperInvariant()}, 'Inbox', 0, 1, {Sql(now)})
                """,
                ct);
            pastDue = await InsertDueTaskAsync(context, listId, "past Due", "2026-10-15", null);
            dueAhead = await InsertDueTaskAsync(context, listId, "Due ahead", "2026-10-16", "10:00:00");
            armedPast = await InsertDueTaskAsync(
                context, listId, "armed past", "2026-10-14", null, notifiedThrough: now.AddDays(-3),
                reminder: now.AddDays(-1));
            snoozed = await InsertDueTaskAsync(
                context, listId, "snoozed", "2026-10-15", "12:00:00", notifiedThrough: now.AddDays(-1),
                snoozed: now.AddMinutes(10));
        }

        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);

        Assert.All(await db.QueryAsync(c => c.Tasks), t => Assert.True(t.NotifyAtDue, t.Title));
        var reminders = db.Get<IReminderService>();
        var due = Assert.Single(await reminders.ListDueAsync(ct));
        Assert.Equal(armedPast, due.TaskId);
        Assert.True(await reminders.TryMarkFiredAsync(due.TaskId, due.DueAtUtc, ct));
        Assert.Empty(await reminders.ListDueAsync(ct));

        db.Clock.Advance(TimeSpan.FromMinutes(10)); // 09:40: the Snooze ends
        due = Assert.Single(await reminders.ListDueAsync(ct));
        Assert.Equal((snoozed, now.AddMinutes(10)), (due.TaskId, due.DueAtUtc));
        Assert.True(await reminders.TryMarkFiredAsync(due.TaskId, due.DueAtUtc, ct));

        db.Clock.Advance(TimeSpan.FromMinutes(20)); // 10:00: the Due still ahead at the upgrade notifies
        due = Assert.Single(await reminders.ListDueAsync(ct));
        Assert.Equal((dueAhead, now.AddMinutes(30)), (due.TaskId, due.DueAtUtc));
        Assert.DoesNotContain(pastDue, (await reminders.ListDueAsync(ct)).Select(d => d.TaskId));
    }

    // ---- Review 1: the upgrade rule of the Due notification is durable ----

    [Fact]
    public async Task An_upgrade_interrupted_after_the_migration_counts_past_Dues_as_notified_on_the_next_start()
    {
        // Now is 2026-10-16 09:30 UTC; the time zone is UTC and the default Reminder time 09:00.
        var ct = TestContext.Current.CancellationToken;
        using var db = new TempDatabase();
        db.Paths.EnsureDirectories();
        var now = db.Clock.GetUtcNow().UtcDateTime;
        Guid dueAhead;
        await using (var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(ct))
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20261020235855_Ticket117_BeforeDue", ct);
            var listId = Guid.NewGuid();
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO Lists (Id, Name, SortOrder, IsDefault, CreatedUtc)
                VALUES ({listId.ToString().ToUpperInvariant()}, 'Inbox', 0, 1, {Sql(now)})
                """,
                ct);
            await InsertDueTaskAsync(context, listId, "past Due", "2026-10-15", null);
            dueAhead = await InsertDueTaskAsync(context, listId, "Due ahead", "2026-10-16", "10:00:00");

            // The start that applied the migrations stopped before the upgrade rule ran.
            await migrator.MigrateAsync(cancellationToken: ct);
        }

        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);

        var reminders = db.Get<IReminderService>();
        Assert.Empty(await reminders.ListDueAsync(ct));
        db.Clock.Advance(TimeSpan.FromMinutes(30)); // 10:00: the Due still ahead at the upgrade notifies
        var due = Assert.Single(await reminders.ListDueAsync(ct));
        Assert.Equal((dueAhead, now.AddMinutes(30)), (due.TaskId, due.DueAtUtc));
    }

    [Fact]
    public async Task A_Due_missed_while_the_app_was_closed_after_the_upgrade_still_notifies_on_the_next_start()
    {
        // Now is 2026-10-16 09:30 UTC; the time zone is UTC and the default Reminder time 09:00.
        var ct = TestContext.Current.CancellationToken;
        using var db = new TempDatabase();
        var initializer = db.Get<IDatabaseInitializer>();
        await initializer.InitializeAsync(ct);
        var listId = Assert.Single(await db.QueryAsync(c => c.Lists)).Id;
        Guid dueAhead;
        await using (var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(ct))
        {
            dueAhead = await InsertDueTaskAsync(
                context, listId, "Due ahead", "2026-10-16", "10:00:00",
                notifiedThrough: db.Clock.GetUtcNow().UtcDateTime);
        }

        db.Clock.Advance(TimeSpan.FromHours(1)); // 10:30: the app was closed at 10:00
        await initializer.InitializeAsync(ct);

        var due = Assert.Single(await db.Get<IReminderService>().ListDueAsync(ct));
        Assert.Equal(dueAhead, due.TaskId);
    }

    [Fact]
    public async Task A_failing_upgrade_rule_does_not_stop_startup_and_is_retried_on_the_next_start()
    {
        var ct = TestContext.Current.CancellationToken;
        var settings = new ThrowingSettingsStore { Fails = true };
        using var db = new TempDatabase(services => services.AddSingleton<IShellSettingsStore>(settings));
        var initializer = db.Get<IDatabaseInitializer>();

        await initializer.InitializeAsync(ct);

        Assert.Equal("Inbox", Assert.Single(await db.QueryAsync(c => c.Lists)).Name);
        Assert.Equal(0, await UserVersionAsync(db, ct));

        settings.Fails = false;
        await initializer.InitializeAsync(ct);

        Assert.Equal(1, await UserVersionAsync(db, ct));
    }

    [Fact]
    public async Task Cancellation_during_the_upgrade_rule_still_propagates()
    {
        using var cts = new CancellationTokenSource();
        var settings = new ThrowingSettingsStore { OnLoad = cts.Cancel };
        using var db = new TempDatabase(services => services.AddSingleton<IShellSettingsStore>(settings));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => db.Get<IDatabaseInitializer>().InitializeAsync(cts.Token));
    }

    private static async Task<long> UserVersionAsync(TempDatabase db, CancellationToken ct)
    {
        await using var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(ct);
        return await context.Database
            .SqlQueryRaw<long>("SELECT user_version AS \"Value\" FROM pragma_user_version")
            .SingleAsync(ct);
    }

    private sealed class ThrowingSettingsStore : IShellSettingsStore
    {
        public bool Fails { get; set; }
        public Action? OnLoad { get; set; }

        public ShellSettings Load()
        {
            OnLoad?.Invoke();
            return Fails ? throw new InvalidOperationException("settings unreadable") : new ShellSettings();
        }

        public void Save(ShellSettings settings)
        {
        }
    }
}
