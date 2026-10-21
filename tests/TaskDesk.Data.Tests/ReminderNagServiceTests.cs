using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public class ReminderNagServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The clock starts at 2026-10-16 09:30Z, in UTC, with the default Reminder time at 09:00.
    private static readonly DateOnly Today = new(2026, 10, 16);

    private static DateTime At(int hour, int minute = 0, int day = 16) =>
        new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    private static async Task<TempDatabase> StartAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return db;
    }

    private static void SetNow(TempDatabase db, DateTime utc) => db.Clock.SetUtcNow(new DateTimeOffset(utc));

    private static async Task<TaskItem> AddDueAsync(TempDatabase db, string title, DateOnly date, TimeOnly? time)
    {
        var task = await db.Get<ITaskService>().CreateTaskAsync(title, Ct);
        return await db.Get<IDueService>().SetDueAsync(task.Id, date, time, Ct);
    }

    // A Task due today at 10:00 with an hourly Nag, notified at its Due.
    private static async Task<TaskItem> HourlyNagAsync(TempDatabase db)
    {
        var task = await AddDueAsync(db, "a", Today, new TimeOnly(10, 0));
        await db.Get<IReminderService>().SetNagPeriodAsync(task.Id, NagPeriod.OneHour, Ct);
        SetNow(db, At(10));
        await ClaimAsync(db, At(10));
        return task;
    }

    // Lists the one due Task, checks its due instant and claims it.
    private static async Task<DueReminder> ClaimAsync(TempDatabase db, DateTime expected)
    {
        var reminders = db.Get<IReminderService>();
        var due = Assert.Single(await reminders.ListDueAsync(Ct));
        Assert.Equal(expected, due.DueAtUtc);
        Assert.True(await reminders.TryMarkFiredAsync(due.TaskId, due.DueAtUtc, Ct));
        return due;
    }

    private static async Task<TaskItem> StoredAsync(TempDatabase db, Guid taskId) =>
        Assert.Single(await db.QueryAsync(c => c.Tasks.Include(t => t.Reminders).Where(t => t.Id == taskId)));

    [Fact]
    public async Task A_new_Task_has_no_Nag()
    {
        using var db = await StartAsync();
        var inbox = Assert.Single(await db.QueryAsync(c => c.Lists)).Id;

        var created = await db.Get<ITaskService>().CreateTaskAsync("a", Ct);
        var quick = await db.Get<ITaskCreator>().CreateTaskInListAsync("b", inbox, Today.AddDays(1), Ct);

        Assert.Null((await StoredAsync(db, created.Id)).NagPeriod);
        Assert.Null((await StoredAsync(db, quick.Id)).NagPeriod);
    }

    [Fact]
    public async Task A_Nag_notifies_every_period_after_the_Due_and_says_Overdue()
    {
        using var db = await StartAsync();
        var task = await HourlyNagAsync(db);
        var reminders = db.Get<IReminderService>();

        Assert.Equal(NagPeriod.OneHour, (await StoredAsync(db, task.Id)).NagPeriod);
        SetNow(db, At(10, 59));
        Assert.Empty(await reminders.ListDueAsync(Ct));
        SetNow(db, At(11));
        Assert.True((await ClaimAsync(db, At(11))).IsOverdue);
        SetNow(db, At(12));
        await ClaimAsync(db, At(12));
    }

    [Fact]
    public async Task A_Nag_notifies_with_Notify_at_Due_off()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Today, new TimeOnly(10, 0));
        var reminders = db.Get<IReminderService>();
        await reminders.SetNotifyAtDueAsync(task.Id, false, Ct);
        await reminders.SetNagPeriodAsync(task.Id, NagPeriod.ThirtyMinutes, Ct);

        SetNow(db, At(10));
        Assert.Empty(await reminders.ListDueAsync(Ct));
        SetNow(db, At(10, 30));
        await ClaimAsync(db, At(10, 30));
    }

    [Fact]
    public async Task After_the_PC_was_off_one_Nag_fires_and_the_schedule_continues_from_the_Due()
    {
        using var db = await StartAsync();
        await HourlyNagAsync(db);
        var reminders = db.Get<IReminderService>();

        SetNow(db, At(15, 20)); // off from 10:30 to 15:20
        await ClaimAsync(db, At(15));

        Assert.Empty(await reminders.ListDueAsync(Ct));
        SetNow(db, At(16));
        await ClaimAsync(db, At(16));
        SetNow(db, At(17));
        await ClaimAsync(db, At(17));
    }

    [Fact]
    public async Task A_Snooze_absorbs_Nags_and_the_schedule_then_resumes()
    {
        using var db = await StartAsync();
        var task = await HourlyNagAsync(db);
        var reminders = db.Get<IReminderService>();
        SetNow(db, At(15, 20));
        await ClaimAsync(db, At(15));

        await reminders.SnoozeAsync(task.Id, ReminderCalculator.LongSnooze, Ct);

        SetNow(db, At(16, 19));
        Assert.Empty(await reminders.ListDueAsync(Ct));
        SetNow(db, At(16, 20));
        await ClaimAsync(db, At(16, 20));
        SetNow(db, At(16, 59));
        Assert.Empty(await reminders.ListDueAsync(Ct));
        SetNow(db, At(17));
        await ClaimAsync(db, At(17));
    }

    [Fact]
    public async Task Completing_stops_the_Nag_and_un_completing_resumes_it_with_no_backlog()
    {
        using var db = await StartAsync();
        var task = await HourlyNagAsync(db);
        var reminders = db.Get<IReminderService>();
        var tasks = db.Get<ITaskService>();
        SetNow(db, At(10, 10));

        await tasks.SetCompletedAsync(task.Id, true, Ct);
        SetNow(db, At(15, 30));
        Assert.Empty(await reminders.ListDueAsync(Ct));
        await tasks.SetCompletedAsync(task.Id, false, Ct);

        Assert.Empty(await reminders.ListDueAsync(Ct));
        SetNow(db, At(16));
        await ClaimAsync(db, At(16));
    }

    [Fact]
    public async Task Picking_Off_stops_the_Nag()
    {
        using var db = await StartAsync();
        var task = await HourlyNagAsync(db);
        var reminders = db.Get<IReminderService>();

        var updated = await reminders.SetNagPeriodAsync(task.Id, null, Ct);
        SetNow(db, At(18));

        Assert.Null(updated.NagPeriod);
        Assert.Null((await StoredAsync(db, task.Id)).NagPeriod);
        Assert.Empty(await reminders.ListDueAsync(Ct));
    }

    [Fact]
    public async Task Moving_the_Due_later_restarts_the_Nag_from_the_new_Due()
    {
        using var db = await StartAsync();
        var task = await HourlyNagAsync(db);
        var reminders = db.Get<IReminderService>();
        SetNow(db, At(11, 30));
        await ClaimAsync(db, At(11));

        await db.Get<IDueService>().SetDueAsync(task.Id, Today, new TimeOnly(14, 0), Ct);

        SetNow(db, At(13, 59));
        Assert.Empty(await reminders.ListDueAsync(Ct));
        SetNow(db, At(14));
        await ClaimAsync(db, At(14)); // the Due notification
        SetNow(db, At(15));
        await ClaimAsync(db, At(15));
    }

    [Fact]
    public async Task A_Nag_on_a_Task_without_a_Due_is_refused()
    {
        using var db = await StartAsync();
        var task = await db.Get<ITaskService>().CreateTaskAsync("a", Ct);

        await Assert.ThrowsAsync<ArgumentException>(
            () => db.Get<IReminderService>().SetNagPeriodAsync(task.Id, NagPeriod.OneDay, Ct));

        Assert.Null((await StoredAsync(db, task.Id)).NagPeriod);
    }

    [Fact]
    public async Task Setting_the_Nag_of_a_missing_Task_throws()
    {
        using var db = await StartAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => db.Get<IReminderService>().SetNagPeriodAsync(Guid.NewGuid(), NagPeriod.OneDay, Ct));
    }

    [Fact]
    public async Task Clearing_the_Due_turns_the_Nag_off_in_the_same_save()
    {
        using var db = await StartAsync();
        var task = await HourlyNagAsync(db);

        var cleared = await db.Get<IDueService>().SetDueAsync(task.Id, null, null, Ct);
        SetNow(db, At(18));

        Assert.Null(cleared.NagPeriod);
        Assert.Null((await StoredAsync(db, task.Id)).NagPeriod);
        Assert.Empty(await db.Get<IReminderService>().ListDueAsync(Ct));
    }

    [Fact]
    public async Task The_next_Occurrence_copies_the_Nag()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Today, null);
        await db.Get<IReminderService>().SetNagPeriodAsync(task.Id, NagPeriod.ThreeHours, Ct);
        await db.Get<IRecurrenceService>().SetRecurrenceAsync(task.Id, Recurrence.Daily(), Ct);

        await db.Get<ITaskService>().SetCompletedAsync(task.Id, true, Ct);

        var next = Assert.Single(await db.QueryAsync(c => c.Tasks.Where(t => !t.IsCompleted)));
        Assert.Equal(NagPeriod.ThreeHours, next.NagPeriod);
    }

    [Fact]
    public async Task Undoing_a_delete_restores_the_Nag()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Today.AddDays(1), null);
        await db.Get<IReminderService>().SetNagPeriodAsync(task.Id, NagPeriod.FifteenMinutes, Ct);
        var tasks = db.Get<ITaskService>();

        var restored = await tasks.RestoreTaskAsync(await tasks.DeleteTaskAsync(task.Id, Ct), Ct);

        Assert.Equal(NagPeriod.FifteenMinutes, restored.NagPeriod);
        Assert.Equal(NagPeriod.FifteenMinutes, (await StoredAsync(db, task.Id)).NagPeriod);
    }

    [Fact]
    public async Task A_Nag_does_not_count_for_Has_reminder()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Today, new TimeOnly(12, 0));
        await db.Get<IReminderService>().SetNagPeriodAsync(task.Id, NagPeriod.OneHour, Ct);

        var found = await db.Get<ITaskSearchService>().SearchAsync(new TaskSearchQuery(HasReminder: true), Ct);

        Assert.Empty(found);
    }

    [Fact]
    public async Task A_Due_notification_shown_late_at_wake_reads_plain()
    {
        using var db = await StartAsync();
        await AddDueAsync(db, "a", Today, new TimeOnly(12, 0));

        SetNow(db, At(8, day: 17)); // asleep through the Due

        var due = Assert.Single(await db.Get<IReminderService>().ListDueAsync(Ct));
        Assert.Equal((At(12), false), (due.DueAtUtc, due.IsOverdue));
    }

    [Fact]
    public async Task A_daily_Nag_on_a_date_only_Due_says_Overdue()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Today.AddDays(1), null);
        await db.Get<IReminderService>().SetNagPeriodAsync(task.Id, NagPeriod.OneDay, Ct);
        SetNow(db, At(9, day: 17));
        Assert.False((await ClaimAsync(db, At(9, day: 17))).IsOverdue); // the Due notification, on time

        SetNow(db, At(9, day: 18));

        Assert.True((await ClaimAsync(db, At(9, day: 18))).IsOverdue);
    }
}
