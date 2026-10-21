using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public class ReminderDueNotificationServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The clock starts at 2026-10-16 09:30Z, in UTC, with the default Reminder time at 09:00.
    private static readonly DateTime Start = new(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 16);

    private static async Task<TempDatabase> StartAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return db;
    }

    private static async Task<TaskItem> AddDueAsync(TempDatabase db, string title, DateOnly date, TimeOnly? time)
    {
        var task = await db.Get<ITaskService>().CreateTaskAsync(title, Ct);
        return await db.Get<IDueService>().SetDueAsync(task.Id, date, time, Ct);
    }

    private static async Task<TaskItem> StoredAsync(TempDatabase db, Guid taskId) =>
        Assert.Single(await db.QueryAsync(c => c.Tasks.Include(t => t.Reminders).Where(t => t.Id == taskId)));

    [Fact]
    public async Task A_new_Task_has_Notify_at_Due_on()
    {
        using var db = await StartAsync();
        var inbox = Assert.Single(await db.QueryAsync(c => c.Lists)).Id;

        var created = await db.Get<ITaskService>().CreateTaskAsync("a", Ct);
        var quick = await db.Get<ITaskCreator>().CreateTaskInListAsync("b", inbox, Today.AddDays(1), Ct);

        Assert.True((await StoredAsync(db, created.Id)).NotifyAtDue);
        Assert.True((await StoredAsync(db, quick.Id)).NotifyAtDue);
    }

    [Fact]
    public async Task A_Task_with_a_Due_notifies_once_at_its_Due_moment()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Today, new TimeOnly(12, 0));
        var reminders = db.Get<IReminderService>();

        db.Clock.SetUtcNow(new DateTimeOffset(Start.AddHours(2).AddMinutes(29)));
        Assert.Empty(await reminders.ListDueAsync(Ct));
        db.Clock.SetUtcNow(new DateTimeOffset(Start.AddHours(2).AddMinutes(30)));
        var due = Assert.Single(await reminders.ListDueAsync(Ct));

        Assert.Equal((task.Id, Start.AddHours(2).AddMinutes(30), false), (due.TaskId, due.DueAtUtc, due.IsOverdue));
        Assert.True(await reminders.TryMarkFiredAsync(due.TaskId, due.DueAtUtc, Ct));
        db.Clock.Advance(TimeSpan.FromDays(3));
        Assert.Empty(await reminders.ListDueAsync(Ct));
    }

    [Fact]
    public async Task A_date_only_Due_notifies_at_the_default_Reminder_time()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Today.AddDays(1), null);

        db.Clock.SetUtcNow(new DateTimeOffset(new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc)));

        var due = Assert.Single(await db.Get<IReminderService>().ListDueAsync(Ct));
        Assert.Equal((task.Id, new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc)), (due.TaskId, due.DueAtUtc));
    }

    [Fact]
    public async Task A_listed_Task_that_is_Overdue_says_so_whatever_made_it_fire()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Today, new TimeOnly(12, 0));
        await db.Get<IReminderService>().AddReminderAsync(
            task.Id, ReminderValue.Fixed(Start.AddHours(5)), Ct); // 14:30, after the Due
        await db.Get<IReminderService>().SetNotifyAtDueAsync(task.Id, false, Ct);

        db.Clock.SetUtcNow(new DateTimeOffset(Start.AddHours(5)));

        var due = Assert.Single(await db.Get<IReminderService>().ListDueAsync(Ct));
        Assert.True(due.IsOverdue);
    }

    [Fact]
    public async Task Switching_Notify_at_Due_off_stores_it_and_the_Due_never_notifies()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Today, new TimeOnly(12, 0));

        var updated = await db.Get<IReminderService>().SetNotifyAtDueAsync(task.Id, false, Ct);
        db.Clock.Advance(TimeSpan.FromDays(1));

        Assert.False(updated.NotifyAtDue);
        Assert.False((await StoredAsync(db, task.Id)).NotifyAtDue);
        Assert.Empty(await db.Get<IReminderService>().ListDueAsync(Ct));
    }

    [Fact]
    public async Task Switching_Notify_at_Due_on_counts_moments_already_past_as_notified()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Today, new TimeOnly(12, 0));
        var reminders = db.Get<IReminderService>();
        await reminders.SetNotifyAtDueAsync(task.Id, false, Ct);
        await reminders.AddReminderAsync(task.Id, ReminderValue.Fixed(Start.AddHours(1)), Ct);
        db.Clock.SetUtcNow(new DateTimeOffset(Start.AddHours(4))); // the Reminder and the Due are both past

        await reminders.SetNotifyAtDueAsync(task.Id, true, Ct);

        Assert.Empty(await reminders.ListDueAsync(Ct));
        Assert.Equal(Start.AddHours(4), (await StoredAsync(db, task.Id)).NotifiedThroughUtc);
    }

    [Fact]
    public async Task Switching_Notify_at_Due_of_a_missing_Task_throws()
    {
        using var db = await StartAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => db.Get<IReminderService>().SetNotifyAtDueAsync(Guid.NewGuid(), false, Ct));
    }

    [Fact]
    public async Task A_Due_edit_with_Notify_at_Due_on_counts_past_moments_as_notified()
    {
        using var db = await StartAsync();
        var task = await db.Get<ITaskService>().CreateTaskAsync("a", Ct);
        await db.Get<IReminderService>().AddReminderAsync(task.Id, ReminderValue.Fixed(Start.AddHours(1)), Ct);
        db.Clock.SetUtcNow(new DateTimeOffset(Start.AddHours(2))); // the Reminder is missed

        await db.Get<IDueService>().SetDueAsync(task.Id, Today.AddDays(4), null, Ct);

        Assert.Empty(await db.Get<IReminderService>().ListDueAsync(Ct));
    }

    [Fact]
    public async Task Moving_the_Due_later_re_arms_the_Due_notification()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Today, new TimeOnly(12, 0));
        var reminders = db.Get<IReminderService>();
        db.Clock.SetUtcNow(new DateTimeOffset(Start.AddHours(3)));
        var due = Assert.Single(await reminders.ListDueAsync(Ct));
        Assert.True(await reminders.TryMarkFiredAsync(due.TaskId, due.DueAtUtc, Ct));

        await db.Get<IDueService>().SetDueAsync(task.Id, Today, new TimeOnly(14, 0), Ct);

        Assert.Empty(await reminders.ListDueAsync(Ct));
        db.Clock.SetUtcNow(new DateTimeOffset(Start.AddHours(4).AddMinutes(30)));
        Assert.Equal(task.Id, Assert.Single(await reminders.ListDueAsync(Ct)).TaskId);
    }

    [Fact]
    public async Task Clearing_the_Due_keeps_Notify_at_Due_off()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Today.AddDays(1), null);
        await db.Get<IReminderService>().SetNotifyAtDueAsync(task.Id, false, Ct);
        var due = db.Get<IDueService>();

        await due.SetDueAsync(task.Id, null, null, Ct);
        var again = await due.SetDueAsync(task.Id, Today.AddDays(2), null, Ct);

        Assert.False(again.NotifyAtDue);
        Assert.False((await StoredAsync(db, task.Id)).NotifyAtDue);
    }

    [Fact]
    public async Task The_next_Occurrence_copies_Notify_at_Due()
    {
        using var db = await StartAsync();
        var on = await AddDueAsync(db, "on", Today, null);
        var off = await AddDueAsync(db, "off", Today, null);
        await db.Get<IReminderService>().SetNotifyAtDueAsync(off.Id, false, Ct);
        foreach (var id in new[] { on.Id, off.Id })
        {
            await db.Get<IRecurrenceService>().SetRecurrenceAsync(id, Recurrence.Daily(), Ct);
            await db.Get<ITaskService>().SetCompletedAsync(id, true, Ct);
        }

        var next = (await db.QueryAsync(c => c.Tasks.Where(t => !t.IsCompleted))).ToDictionary(t => t.Title);

        Assert.True(next["on"].NotifyAtDue);
        Assert.False(next["off"].NotifyAtDue);
    }

    [Fact]
    public async Task Undoing_a_delete_restores_Notify_at_Due()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db, "a", Today.AddDays(1), null);
        await db.Get<IReminderService>().SetNotifyAtDueAsync(task.Id, false, Ct);
        var tasks = db.Get<ITaskService>();

        var restored = await tasks.RestoreTaskAsync(await tasks.DeleteTaskAsync(task.Id, Ct), Ct);

        Assert.False(restored.NotifyAtDue);
        Assert.False((await StoredAsync(db, task.Id)).NotifyAtDue);
    }

    [Fact]
    public async Task The_Due_notification_does_not_count_for_Has_reminder()
    {
        using var db = await StartAsync();
        await AddDueAsync(db, "a", Today, new TimeOnly(12, 0));

        var found = await db.Get<ITaskSearchService>().SearchAsync(
            new TaskSearchQuery(HasReminder: true), Ct);

        Assert.Empty(found);
    }

    [Fact]
    public async Task One_check_over_5000_open_Tasks_with_a_Due_stays_far_inside_the_interval()
    {
        using var db = await StartAsync();
        var inbox = Assert.Single(await db.QueryAsync(c => c.Lists)).Id;
        await using (var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(Ct))
        {
            // Every Due is already past and counts as notified: each one is a candidate the rule runs over.
            for (var i = 0; i < 5000; i++)
            {
                context.Tasks.Add(new TaskItem
                {
                    Id = Guid.CreateVersion7(),
                    ListId = inbox,
                    Title = $"Task {i}",
                    DueDate = Today.AddDays(-(i % 60)),
                    DueTime = i % 2 == 0 ? new TimeOnly(8, 0) : null,
                    NotifiedThroughUtc = Start,
                    SortOrder = i,
                    CreatedUtc = Start,
                    UpdatedUtc = Start,
                });
            }

            await context.SaveChangesAsync(Ct);
        }

        var reminders = db.Get<IReminderService>();
        await reminders.ListDueAsync(Ct); // warm up
        var watch = Stopwatch.StartNew();
        var due = await reminders.ListDueAsync(Ct);
        watch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"One check over 5,000 open Tasks with a Due: {watch.ElapsedMilliseconds} ms");
        Assert.Empty(due);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"{watch.ElapsedMilliseconds} ms");
    }
}
