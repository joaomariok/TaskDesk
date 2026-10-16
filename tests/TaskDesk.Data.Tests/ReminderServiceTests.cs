using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public class ReminderServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The clock starts at 2026-10-16 09:30Z.
    private static readonly DateTime Now = new(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc);

    private static async Task<TempDatabase> StartAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return db;
    }

    private static async Task<TaskItem> AddAsync(TempDatabase db, string title, DateTime? reminderUtc)
    {
        var task = await db.Get<ITaskService>().CreateTaskAsync(title, Ct);
        return reminderUtc is null ? task : await db.Get<IReminderService>().SetReminderAsync(task.Id, reminderUtc, Ct);
    }

    // Snooze is a later ticket; this arranges a snoozed Reminder directly.
    private static async Task SnoozeAsync(TempDatabase db, Guid taskId, DateTime untilUtc)
    {
        await using var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(Ct);
        var task = await context.Tasks.SingleAsync(t => t.Id == taskId, Ct);
        task.SnoozedUntilUtc = untilUtc;
        task.ReminderFiredUtc = null;
        await context.SaveChangesAsync(Ct);
    }

    private static async Task<List<string>> DueTitlesAsync(TempDatabase db) =>
        [.. (await db.Get<IReminderService>().ListDueAsync(Ct)).Select(r => r.Title)];

    [Fact]
    public async Task A_Reminder_is_stored_as_a_UTC_instant_and_can_be_cleared()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", null);
        var at = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        var updated = await db.Get<IReminderService>().SetReminderAsync(task.Id, at, Ct);
        var stored = Assert.Single(await db.Get<ITaskService>().ListDefaultListTasksAsync(Ct));

        Assert.Equal(at, updated.ReminderUtc);
        Assert.Equal(at, stored.ReminderUtc);
        Assert.Equal(DateTimeKind.Utc, stored.ReminderUtc!.Value.Kind);

        await db.Get<IReminderService>().SetReminderAsync(task.Id, null, Ct);
        Assert.Null(Assert.Single(await db.Get<ITaskService>().ListDefaultListTasksAsync(Ct)).ReminderUtc);
    }

    [Fact]
    public async Task Setting_the_Reminder_of_a_missing_Task_throws()
    {
        using var db = await StartAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => db.Get<IReminderService>().SetReminderAsync(Guid.NewGuid(), Now, Ct));
    }

    [Fact]
    public async Task Only_due_armed_open_Reminders_are_listed_first_one_first()
    {
        using var db = await StartAsync();
        await AddAsync(db, "later due", Now.AddMinutes(-1));
        await AddAsync(db, "due now", Now);
        await AddAsync(db, "earlier due", Now.AddHours(-5));
        await AddAsync(db, "not due yet", Now.AddSeconds(1));
        await AddAsync(db, "no reminder", null);
        var fired = await AddAsync(db, "already fired", Now.AddMinutes(-10));
        await db.Get<IReminderService>().TryMarkFiredAsync(fired.Id, Now.AddMinutes(-10), Ct);
        var completed = await AddAsync(db, "completed", Now.AddMinutes(-10));
        await db.Get<ITaskService>().SetCompletedAsync(completed.Id, true, Ct);

        Assert.Equal(["earlier due", "later due", "due now"], await DueTitlesAsync(db));
    }

    [Fact]
    public async Task A_due_Reminder_carries_the_Task_title_Due_List_name_and_the_instant_it_fired_at()
    {
        using var db = await StartAsync();
        var list = await db.Get<IListService>().CreateListAsync("Home", Ct);
        var task = await db.Get<ITaskCreator>().CreateTaskInListAsync("Pay rent", list.Id, new DateOnly(2026, 10, 20), Ct);
        await db.Get<IDueService>().SetDueAsync(task.Id, new DateOnly(2026, 10, 20), new TimeOnly(14, 30), Ct);
        await db.Get<IReminderService>().SetReminderAsync(task.Id, Now.AddMinutes(-1), Ct);

        var due = Assert.Single(await db.Get<IReminderService>().ListDueAsync(Ct));

        Assert.Equal(
            new DueReminder(task.Id, "Pay rent", new DateOnly(2026, 10, 20), new TimeOnly(14, 30), "Home", Now.AddMinutes(-1)),
            due);
    }

    [Fact]
    public async Task A_Snooze_is_the_instant_the_Reminder_fires_at()
    {
        using var db = await StartAsync();
        var later = await AddAsync(db, "snoozed to later", Now.AddHours(-1));
        await SnoozeAsync(db, later.Id, Now.AddMinutes(10));
        var passed = await AddAsync(db, "snooze passed", Now.AddHours(1));
        await SnoozeAsync(db, passed.Id, Now.AddMinutes(-1));

        var due = Assert.Single(await db.Get<IReminderService>().ListDueAsync(Ct));

        Assert.Equal("snooze passed", due.Title);
        Assert.Equal(Now.AddMinutes(-1), due.DueAtUtc);
    }

    [Fact]
    public async Task A_Reminder_fires_once()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddMinutes(-1));
        var reminders = db.Get<IReminderService>();

        Assert.True(await reminders.TryMarkFiredAsync(task.Id, Now.AddMinutes(-1), Ct));
        Assert.False(await reminders.TryMarkFiredAsync(task.Id, Now.AddMinutes(-1), Ct));

        Assert.Empty(await DueTitlesAsync(db));
        var stored = Assert.Single(await db.Get<ITaskService>().ListDefaultListTasksAsync(Ct));
        Assert.Equal(Now, stored.ReminderFiredUtc);
    }

    [Fact]
    public async Task A_Reminder_edited_after_it_was_listed_is_not_marked_fired()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddMinutes(-1));
        var reminders = db.Get<IReminderService>();
        await reminders.SetReminderAsync(task.Id, Now.AddMinutes(-2), Ct);

        Assert.False(await reminders.TryMarkFiredAsync(task.Id, Now.AddMinutes(-1), Ct));

        Assert.Equal(["a"], await DueTitlesAsync(db));
    }

    [Fact]
    public async Task Editing_a_fired_Reminder_re_arms_it()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddMinutes(-1));
        var reminders = db.Get<IReminderService>();
        await reminders.TryMarkFiredAsync(task.Id, Now.AddMinutes(-1), Ct);

        var updated = await reminders.SetReminderAsync(task.Id, Now.AddMinutes(5), Ct);
        Assert.Null(updated.ReminderFiredUtc);
        Assert.Empty(await DueTitlesAsync(db));

        db.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(["a"], await DueTitlesAsync(db));
    }

    [Fact]
    public async Task Setting_the_same_Reminder_again_keeps_it_fired()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddMinutes(-1));
        var reminders = db.Get<IReminderService>();
        await reminders.TryMarkFiredAsync(task.Id, Now.AddMinutes(-1), Ct);

        await reminders.SetReminderAsync(task.Id, Now.AddMinutes(-1), Ct);

        Assert.Empty(await DueTitlesAsync(db));
    }

    [Fact]
    public async Task Editing_the_Reminder_clears_the_Snooze()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddHours(-1));
        await SnoozeAsync(db, task.Id, Now.AddHours(1));

        var updated = await db.Get<IReminderService>().SetReminderAsync(task.Id, Now.AddMinutes(-1), Ct);

        Assert.Null(updated.SnoozedUntilUtc);
        Assert.Equal(["a"], await DueTitlesAsync(db));
    }

    [Fact]
    public async Task Completing_a_Task_clears_its_Snooze()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddHours(-1));
        await SnoozeAsync(db, task.Id, Now.AddHours(1));

        var completed = await db.Get<ITaskService>().SetCompletedAsync(task.Id, true, Ct);

        Assert.Null(completed.SnoozedUntilUtc);
        Assert.Equal(Now.AddHours(-1), completed.ReminderUtc);
    }

    [Fact]
    public async Task Undoing_a_delete_restores_the_Reminder_with_its_state()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddHours(-1));
        await SnoozeAsync(db, task.Id, Now.AddHours(1));
        await db.Get<IReminderService>().TryMarkFiredAsync(task.Id, Now.AddHours(1), Ct); // not due: unchanged
        var service = db.Get<ITaskService>();

        var deleted = await service.DeleteTaskAsync(task.Id, Ct);
        var restored = await service.RestoreTaskAsync(deleted, Ct);

        Assert.Equal(Now.AddHours(-1), restored.ReminderUtc);
        Assert.Equal(Now.AddHours(1), restored.SnoozedUntilUtc);
        Assert.Null(restored.ReminderFiredUtc);
    }

    [Fact]
    public async Task Undoing_a_delete_keeps_a_fired_Reminder_fired()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddMinutes(-1));
        await db.Get<IReminderService>().TryMarkFiredAsync(task.Id, Now.AddMinutes(-1), Ct);
        var service = db.Get<ITaskService>();

        await service.RestoreTaskAsync(await service.DeleteTaskAsync(task.Id, Ct), Ct);

        Assert.Empty(await DueTitlesAsync(db));
    }

    [Fact]
    public async Task Saving_a_Task_raises_TasksChanged_and_other_saves_do_not()
    {
        using var db = await StartAsync();
        var raised = 0;
        db.Get<ITaskChangeNotifier>().TasksChanged += () => Interlocked.Increment(ref raised);

        await db.Get<IListService>().CreateListAsync("Home", Ct);
        Assert.Equal(0, raised);

        var task = await db.Get<ITaskService>().CreateTaskAsync("a", Ct);
        Assert.Equal(1, raised);

        await db.Get<IReminderService>().SetReminderAsync(task.Id, Now, Ct);
        Assert.Equal(2, raised);
    }
}
