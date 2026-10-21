using TaskDesk.Core.Export;
using TaskDesk.Core.Import;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public class ReminderBeforeDueServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Local time is UTC-3. The clock starts at local 2026-10-16 06:30 (09:30 UTC), a Friday.
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test-3", TimeSpan.FromHours(-3), "Test-3", "Test-3");

    private static readonly DateOnly Monday = new(2026, 10, 19);

    private static async Task<TempDatabase> StartAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        db.Clock.SetLocalTimeZone(Zone);
        return db;
    }

    private static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    private static void ClockAt(TempDatabase db, DateTime utc) => db.Clock.SetUtcNow(new DateTimeOffset(utc));

    private static async Task<TaskItem> AddDueAsync(TempDatabase db, string title, DateOnly? due, TimeOnly? time = null)
    {
        var task = await db.Get<ITaskService>().CreateTaskAsync(title, Ct);
        return due is null ? task : await db.Get<IDueService>().SetDueAsync(task.Id, due, time, Ct);
    }

    private static Task<TaskItem> AddBeforeDueAsync(TempDatabase db, Guid taskId, int amount, ReminderUnit unit) =>
        db.Get<IReminderService>().AddReminderAsync(taskId, ReminderValue.BeforeDue(amount, unit), Ct);

    private static async Task<List<(string Title, DateTime At)>> DueAsync(TempDatabase db) =>
        [.. (await db.Get<IReminderService>().ListDueAsync(Ct)).Select(r => (r.Title, r.DueAtUtc))];

    private static async Task<TaskItem> ReadBackAsync(TempDatabase db, Guid taskId) =>
        (await db.Get<ITaskService>().ListDefaultListTasksAsync(Ct)).Single(t => t.Id == taskId);

    [Fact]
    public async Task A_before_Due_Reminder_is_stored_with_its_amount_and_unit_as_given()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Monday);

        var updated = await AddBeforeDueAsync(db, task.Id, 999, ReminderUnit.Weeks);
        var stored = await ReadBackAsync(db, task.Id);

        Assert.Equal([ReminderValue.BeforeDue(999, ReminderUnit.Weeks)], updated.Reminders.Select(r => r.Value));
        Assert.Equal([ReminderValue.BeforeDue(999, ReminderUnit.Weeks)], stored.Reminders.Select(r => r.Value));

        var changed = await db.Get<IReminderService>().UpdateReminderAsync(
            stored.Reminders[0].Id, ReminderValue.BeforeDue(90, ReminderUnit.Minutes), Ct);
        Assert.Equal([ReminderValue.BeforeDue(90, ReminderUnit.Minutes)], changed.Reminders.Select(r => r.Value));
        Assert.Equal(
            [ReminderValue.BeforeDue(90, ReminderUnit.Minutes)],
            (await ReadBackAsync(db, task.Id)).Reminders.Select(r => r.Value));
    }

    [Fact]
    public async Task A_before_Due_Reminder_on_a_Task_without_a_Due_is_refused()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", null);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => AddBeforeDueAsync(db, task.Id, 1, ReminderUnit.Days));

        Assert.Equal("A Reminder before Due needs a Due.", ex.Message);
        Assert.Empty((await ReadBackAsync(db, task.Id)).Reminders);
    }

    [Fact]
    public async Task The_same_amount_and_unit_twice_on_one_Task_is_refused()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Monday);
        await AddBeforeDueAsync(db, task.Id, 1, ReminderUnit.Days);

        await Assert.ThrowsAsync<ArgumentException>(() => AddBeforeDueAsync(db, task.Id, 1, ReminderUnit.Days));

        Assert.Single((await ReadBackAsync(db, task.Id)).Reminders);
    }

    [Fact]
    public async Task A_date_only_Due_counts_from_the_default_Reminder_time()
    {
        // Due Monday, default 09:00 local (12:00 UTC): "1 day before" fires Sunday 09:00, "2 hours before" Monday 07:00.
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Monday);
        await AddBeforeDueAsync(db, task.Id, 1, ReminderUnit.Days);
        await AddBeforeDueAsync(db, task.Id, 2, ReminderUnit.Hours);

        ClockAt(db, Utc(18, 11, 59));
        Assert.Empty(await DueAsync(db));
        ClockAt(db, Utc(18, 12));
        Assert.Equal([("a", Utc(18, 12))], await DueAsync(db));
        Assert.True(await db.Get<IReminderService>().TryMarkFiredAsync(task.Id, Utc(18, 12), Ct));
        ClockAt(db, Utc(19, 10));
        Assert.Equal([("a", Utc(19, 10))], await DueAsync(db));
    }

    [Fact]
    public async Task A_change_of_the_default_Reminder_time_applies_at_once()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Monday);
        await AddBeforeDueAsync(db, task.Id, 1, ReminderUnit.Days);
        var settings = db.Get<IShellSettingsStore>();
        ClockAt(db, Utc(18, 10));
        Assert.Empty(await DueAsync(db)); // at 09:00 local it fires 12:00 UTC

        settings.Save(settings.Load() with { DefaultReminderTime = new TimeOnly(7, 0) });

        Assert.Equal([("a", Utc(18, 10))], await DueAsync(db)); // now 07:00 local, 10:00 UTC
    }

    [Fact]
    public async Task Moving_the_Due_moves_before_Due_Reminders_under_the_past_moment_rule()
    {
        // Due Monday 14:00 local (17:00 UTC), "1 hour before" at 16:00 UTC and "1 day before" Sunday 17:00 UTC.
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Monday, new TimeOnly(14, 0));
        await AddBeforeDueAsync(db, task.Id, 1, ReminderUnit.Hours);
        await AddBeforeDueAsync(db, task.Id, 1, ReminderUnit.Days);

        // At Monday 12:00 UTC the Due moves to Monday 10:00 local (13:00 UTC): "1 day before" moves to Sunday
        // 13:00 UTC and "1 hour before" to 12:00 UTC, both already past, so neither fires.
        ClockAt(db, Utc(19, 12));
        await db.Get<IDueService>().SetDueAsync(task.Id, Monday, new TimeOnly(10, 0), Ct);
        Assert.Empty(await DueAsync(db));

        // Moved later to Tuesday 14:00 local: both fire again, at their new moments.
        await db.Get<IDueService>().SetDueAsync(task.Id, Monday.AddDays(1), new TimeOnly(14, 0), Ct);
        ClockAt(db, Utc(19, 16, 59));
        Assert.Empty(await DueAsync(db));
        ClockAt(db, Utc(19, 17));
        Assert.Equal([("a", Utc(19, 17))], await DueAsync(db));
        Assert.True(await db.Get<IReminderService>().TryMarkFiredAsync(task.Id, Utc(19, 17), Ct));
        ClockAt(db, Utc(20, 16));
        Assert.Equal([("a", Utc(20, 16))], await DueAsync(db));
    }

    [Fact]
    public async Task Clearing_the_Due_deletes_the_before_Due_Reminders_and_keeps_the_fixed_ones()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Monday);
        await AddBeforeDueAsync(db, task.Id, 1, ReminderUnit.Days);
        await db.Get<IReminderService>().AddReminderAsync(task.Id, ReminderValue.Fixed(Utc(20, 12)), Ct);
        await AddBeforeDueAsync(db, task.Id, 30, ReminderUnit.Minutes);

        var cleared = await db.Get<IDueService>().SetDueAsync(task.Id, null, null, Ct);

        Assert.Equal([ReminderValue.Fixed(Utc(20, 12))], cleared.Reminders.Select(r => r.Value));
        Assert.Equal(
            [ReminderValue.Fixed(Utc(20, 12))], (await ReadBackAsync(db, task.Id)).Reminders.Select(r => r.Value));
        Assert.Single(await db.QueryAsync(c => c.Reminders));
    }

    [Fact]
    public async Task The_next_Occurrence_copies_before_Due_Reminders_as_they_are()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Monday, new TimeOnly(14, 0));
        await db.Get<IRecurrenceService>().SetRecurrenceAsync(task.Id, Recurrence.Daily(), Ct);
        await AddBeforeDueAsync(db, task.Id, 2, ReminderUnit.Days);
        await AddBeforeDueAsync(db, task.Id, 15, ReminderUnit.Minutes);

        var completed = await db.Get<ITaskService>().SetCompletedAsync(task.Id, true, Ct);

        var next = await ReadBackAsync(db, completed.NextOccurrenceId!.Value);
        Assert.Equal(
            [ReminderValue.BeforeDue(2, ReminderUnit.Days), ReminderValue.BeforeDue(15, ReminderUnit.Minutes)],
            next.Reminders.Select(r => r.Value).OrderByDescending(v => v.Unit));
        Assert.Equal(2, (await ReadBackAsync(db, task.Id)).Reminders.Count);
    }

    [Fact]
    public async Task Undoing_a_delete_restores_before_Due_Reminders()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Monday);
        var withReminders = await AddBeforeDueAsync(db, task.Id, 3, ReminderUnit.Hours);
        var service = db.Get<ITaskService>();

        var restored = await service.RestoreTaskAsync(await service.DeleteTaskAsync(task.Id, Ct), Ct);

        Assert.Equal(
            withReminders.Reminders.Select(r => (r.Id, r.Value)), restored.Reminders.Select(r => (r.Id, r.Value)));
        Assert.Equal(
            withReminders.Reminders.Select(r => (r.Id, r.Value)),
            (await ReadBackAsync(db, task.Id)).Reminders.Select(r => (r.Id, r.Value)));
    }

    [Fact]
    public async Task A_Task_with_only_before_Due_Reminders_matches_Has_reminder()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "reminded", Monday);
        await AddBeforeDueAsync(db, task.Id, 1, ReminderUnit.Days);
        await AddDueAsync(db, "plain", Monday);

        var found = await db.Get<ITaskSearchService>().SearchAsync(new TaskSearchQuery(HasReminder: true), Ct);

        Assert.Equal(["reminded"], found.Select(t => t.Title));
    }

    [Fact]
    public async Task Export_and_import_carry_before_Due_Reminders()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Monday, new TimeOnly(14, 0));
        await AddBeforeDueAsync(db, task.Id, 2, ReminderUnit.Weeks);
        var path = Path.Combine(db.Paths.DataDirectory, "export.json");
        await db.Get<IExportService>().ExportJsonAsync(path, "1.0.0", Ct);
        using var target = await StartAsync();

        await target.Get<IImportService>().ImportJsonAsync(path, ImportMode.Replace, Ct);

        Assert.Equal(
            [ReminderValue.BeforeDue(2, ReminderUnit.Weeks)], (await ReadBackAsync(target, task.Id)).Reminders.Select(r => r.Value));
    }
}
