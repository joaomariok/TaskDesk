using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public class ReminderServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The clock starts at 2026-10-16 09:30Z. A moment already past when it is set never fires (the past-moment
    // rule), so tests set Reminders while the clock is at Start and then move it to Now.
    private static readonly DateTime Start = new(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Start.AddHours(12);

    private static async Task<TempDatabase> StartAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return db;
    }

    private static void AdvanceToNow(TempDatabase db) => db.Clock.SetUtcNow(new DateTimeOffset(Now));

    private static async Task<TaskItem> AddAsync(TempDatabase db, string title, DateTime? reminderUtc)
    {
        var task = await db.Get<ITaskService>().CreateTaskAsync(title, Ct);
        return reminderUtc is { } at
            ? await db.Get<IReminderService>().AddReminderAsync(task.Id, ReminderValue.Fixed(at), Ct)
            : task;
    }

    private static Guid ReminderId(TaskItem task) => Assert.Single(task.Reminders).Id;

    private static List<DateTime?> Moments(TaskItem task) => [.. task.Reminders.Select(r => r.AtUtc)];

    // A Task whose Reminder at Now - 1 min is due now.
    private static async Task<TaskItem> AddDueAsync(TempDatabase db, string title = "a")
    {
        var task = await AddAsync(db, title, Now.AddMinutes(-1));
        AdvanceToNow(db);
        return task;
    }

    // Arranges a snoozed Task directly.
    private static async Task SnoozeAsync(TempDatabase db, Guid taskId, DateTime untilUtc)
    {
        await using var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(Ct);
        var task = await context.Tasks.SingleAsync(t => t.Id == taskId, Ct);
        task.SnoozedUntilUtc = untilUtc;
        await context.SaveChangesAsync(Ct);
    }

    private static async Task<List<string>> DueTitlesAsync(TempDatabase db) =>
        [.. (await db.Get<IReminderService>().ListDueAsync(Ct)).Select(r => r.Title)];

    private static async Task<TaskItem> StoredAsync(TempDatabase db, Guid taskId) =>
        Assert.Single(await db.QueryAsync(
            c => c.Tasks.Include(t => t.Reminders.OrderBy(r => r.AtUtc)).Where(t => t.Id == taskId)));

    [Fact]
    public async Task A_Reminder_is_stored_as_a_UTC_instant_and_can_be_removed()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", null);
        var at = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        var updated = await db.Get<IReminderService>().AddReminderAsync(task.Id, ReminderValue.Fixed(at), Ct);
        var stored = Assert.Single(await db.Get<ITaskService>().ListDefaultListTasksAsync(Ct));

        Assert.Equal([at], Moments(updated));
        Assert.Equal([at], Moments(stored));
        Assert.Equal(ReminderKind.Fixed, stored.Reminders[0].Kind);
        Assert.Equal(DateTimeKind.Utc, stored.Reminders[0].AtUtc!.Value.Kind);

        var removed = await db.Get<IReminderService>().RemoveReminderAsync(ReminderId(updated), Ct);
        Assert.Empty(removed.Reminders);
        Assert.Empty(Assert.Single(await db.Get<ITaskService>().ListDefaultListTasksAsync(Ct)).Reminders);
    }

    [Fact]
    public async Task A_Task_holds_several_Reminders_the_one_that_fires_first_first()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddHours(2));
        var reminders = db.Get<IReminderService>();

        await reminders.AddReminderAsync(task.Id, ReminderValue.Fixed(Now.AddHours(-1)), Ct);
        var updated = await reminders.AddReminderAsync(task.Id, ReminderValue.Fixed(Now.AddHours(1)), Ct);

        List<DateTime?> expected = [Now.AddHours(-1), Now.AddHours(1), Now.AddHours(2)];
        Assert.Equal(expected, Moments(updated));
        Assert.Equal(expected, Moments(await StoredAsync(db, task.Id)));
        Assert.Equal(expected, Moments(Assert.Single(await db.Get<ITaskService>().ListDefaultListTasksAsync(Ct))));
    }

    [Fact]
    public async Task A_duplicate_Reminder_is_refused_and_changes_nothing()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddHours(1));
        var reminders = db.Get<IReminderService>();
        var other = await reminders.AddReminderAsync(task.Id, ReminderValue.Fixed(Now.AddHours(2)), Ct);
        var otherId = other.Reminders.Single(r => r.AtUtc == Now.AddHours(2)).Id;

        await Assert.ThrowsAsync<ArgumentException>(
            () => reminders.AddReminderAsync(task.Id, ReminderValue.Fixed(Now.AddHours(1)), Ct));
        await Assert.ThrowsAsync<ArgumentException>(
            () => reminders.UpdateReminderAsync(otherId, ReminderValue.Fixed(Now.AddHours(1)), Ct));

        Assert.Equal([Now.AddHours(1), Now.AddHours(2)], Moments(await StoredAsync(db, task.Id)));
    }

    [Fact]
    public async Task The_same_moment_on_two_Tasks_is_not_a_duplicate()
    {
        using var db = await StartAsync();
        await AddAsync(db, "a", Now.AddHours(1));

        var b = await AddAsync(db, "b", Now.AddHours(1));

        Assert.Equal([Now.AddHours(1)], Moments(b));
    }

    [Fact]
    public async Task Adding_a_Reminder_to_a_missing_Task_throws()
    {
        using var db = await StartAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => db.Get<IReminderService>().AddReminderAsync(Guid.NewGuid(), ReminderValue.Fixed(Now), Ct));
    }

    [Fact]
    public async Task Changing_or_removing_a_missing_Reminder_throws()
    {
        using var db = await StartAsync();
        var reminders = db.Get<IReminderService>();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => reminders.UpdateReminderAsync(Guid.NewGuid(), ReminderValue.Fixed(Now), Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => reminders.RemoveReminderAsync(Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task Reminders_are_deleted_with_their_Task()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddHours(1));
        await db.Get<IReminderService>().AddReminderAsync(task.Id, ReminderValue.Fixed(Now.AddHours(2)), Ct);

        await db.Get<ITaskService>().DeleteTaskAsync(task.Id, Ct);

        Assert.Empty(await db.QueryAsync(c => c.Reminders));
    }

    [Fact]
    public async Task Several_Reminders_due_together_give_one_entry_at_the_latest_due_moment()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddHours(-2));
        var reminders = db.Get<IReminderService>();
        await reminders.AddReminderAsync(task.Id, ReminderValue.Fixed(Now.AddMinutes(-1)), Ct);
        await reminders.AddReminderAsync(task.Id, ReminderValue.Fixed(Now.AddMinutes(-30)), Ct);
        await reminders.AddReminderAsync(task.Id, ReminderValue.Fixed(Now.AddMinutes(30)), Ct);
        AdvanceToNow(db);

        var due = Assert.Single(await reminders.ListDueAsync(Ct));
        Assert.Equal(Now.AddMinutes(-1), due.DueAtUtc);
        Assert.True(await reminders.TryMarkFiredAsync(task.Id, due.DueAtUtc, Ct));
        Assert.Empty(await DueTitlesAsync(db));

        db.Clock.Advance(TimeSpan.FromMinutes(30));
        due = Assert.Single(await reminders.ListDueAsync(Ct));
        Assert.Equal(Now.AddMinutes(30), due.DueAtUtc);
    }

    [Fact]
    public async Task Removing_a_Reminder_counts_moments_already_past_as_notified()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddMinutes(-1));
        var reminders = db.Get<IReminderService>();
        var withLater = await reminders.AddReminderAsync(task.Id, ReminderValue.Fixed(Now.AddHours(1)), Ct);
        var later = withLater.Reminders.Single(r => r.AtUtc == Now.AddHours(1)).Id;
        AdvanceToNow(db);
        Assert.Equal(["a"], await DueTitlesAsync(db));

        var removed = await reminders.RemoveReminderAsync(later, Ct);

        Assert.Equal([Now.AddMinutes(-1)], Moments(removed));
        Assert.Empty(await DueTitlesAsync(db));
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
        var completed = await AddAsync(db, "completed", Now.AddMinutes(-10));
        AdvanceToNow(db);
        await db.Get<IReminderService>().TryMarkFiredAsync(fired.Id, Now.AddMinutes(-10), Ct);
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
        await db.Get<IReminderService>().AddReminderAsync(task.Id, ReminderValue.Fixed(Now.AddMinutes(-1)), Ct);
        AdvanceToNow(db);

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
        AdvanceToNow(db);

        var due = Assert.Single(await db.Get<IReminderService>().ListDueAsync(Ct));

        Assert.Equal("snooze passed", due.Title);
        Assert.Equal(Now.AddMinutes(-1), due.DueAtUtc);
    }

    [Fact]
    public async Task A_Reminder_fires_once()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db);
        var reminders = db.Get<IReminderService>();

        Assert.True(await reminders.TryMarkFiredAsync(task.Id, Now.AddMinutes(-1), Ct));
        Assert.False(await reminders.TryMarkFiredAsync(task.Id, Now.AddMinutes(-1), Ct));

        Assert.Empty(await DueTitlesAsync(db));
        Assert.Equal(Now.AddMinutes(-1), (await StoredAsync(db, task.Id)).NotifiedThroughUtc);
    }

    [Fact]
    public async Task Releasing_a_claim_persists_and_the_Reminder_is_due_again()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db);
        var reminders = db.Get<IReminderService>();

        var claim = await reminders.TryClaimAsync(task.Id, Now.AddMinutes(-1), Ct);
        Assert.Equal(Now.AddMinutes(-1), claim);
        Assert.Empty(await DueTitlesAsync(db));

        Assert.True(await reminders.ReleaseClaimAsync(task.Id, claim!.Value, Ct));

        var due = Assert.Single(await reminders.ListDueAsync(Ct));
        Assert.Equal(Now.AddMinutes(-1), due.DueAtUtc);
        Assert.Equal(Now.AddMinutes(-1), await reminders.TryClaimAsync(task.Id, Now.AddMinutes(-1), Ct));
    }

    [Fact]
    public async Task Releasing_releases_only_that_claim()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db);
        var reminders = db.Get<IReminderService>();
        var first = await reminders.TryClaimAsync(task.Id, Now.AddMinutes(-1), Ct);
        await reminders.UpdateReminderAsync(ReminderId(task), ReminderValue.Fixed(Now.AddMinutes(1)), Ct); // re-armed since
        db.Clock.Advance(TimeSpan.FromMinutes(2));
        var second = await reminders.TryClaimAsync(task.Id, Now.AddMinutes(1), Ct);

        Assert.False(await reminders.ReleaseClaimAsync(task.Id, first!.Value, Ct));

        Assert.Equal(second, (await StoredAsync(db, task.Id)).NotifiedThroughUtc);
        Assert.Empty(await DueTitlesAsync(db));
    }

    [Fact]
    public async Task A_refused_claim_returns_no_marker()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db);
        var reminders = db.Get<IReminderService>();
        await reminders.TryClaimAsync(task.Id, Now.AddMinutes(-1), Ct);

        Assert.Null(await reminders.TryClaimAsync(task.Id, Now.AddMinutes(-1), Ct));
    }

    [Fact]
    public async Task A_Reminder_edited_after_it_was_listed_is_not_marked_fired()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db);
        var reminders = db.Get<IReminderService>();
        await reminders.UpdateReminderAsync(ReminderId(task), ReminderValue.Fixed(Now.AddMinutes(1)), Ct);

        Assert.False(await reminders.TryMarkFiredAsync(task.Id, Now.AddMinutes(-1), Ct));

        db.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(["a"], await DueTitlesAsync(db));
    }

    [Fact]
    public async Task A_Task_completed_after_it_was_listed_is_not_claimed()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db);
        await db.Get<ITaskService>().SetCompletedAsync(task.Id, true, Ct);

        Assert.Null(await db.Get<IReminderService>().TryClaimAsync(task.Id, Now.AddMinutes(-1), Ct));
    }

    [Fact]
    public async Task A_Task_snoozed_after_it_was_listed_is_not_claimed()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db);
        var reminders = db.Get<IReminderService>();
        await reminders.SnoozeAsync(task.Id, ReminderCalculator.ShortSnooze, Ct);

        Assert.Null(await reminders.TryClaimAsync(task.Id, Now.AddMinutes(-1), Ct));

        db.Clock.Advance(ReminderCalculator.ShortSnooze);
        var due = Assert.Single(await reminders.ListDueAsync(Ct));
        Assert.Equal(Now + ReminderCalculator.ShortSnooze, due.DueAtUtc);
    }

    [Fact]
    public async Task A_Reminder_set_to_a_moment_already_past_never_fires()
    {
        using var db = await StartAsync();
        AdvanceToNow(db);
        var set = await AddAsync(db, "set in the past", Now.AddMinutes(-1));
        var moved = await AddAsync(db, "moved to the past", Now.AddHours(1));
        await db.Get<IReminderService>().UpdateReminderAsync(
            ReminderId(moved), ReminderValue.Fixed(Now.AddMinutes(-5)), Ct);

        Assert.Empty(await DueTitlesAsync(db));
        db.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Empty(await DueTitlesAsync(db));
        Assert.Equal(Now.AddMinutes(-1), Assert.Single((await StoredAsync(db, set.Id)).Reminders).AtUtc);
    }

    [Fact]
    public async Task A_missed_Reminder_still_fires_after_edits_to_other_fields()
    {
        // "Notify at Due" is off, so no moment of this Task depends on its Due and the Due edit is like any other
        // field edit (with it on, a Due edit counts past moments as notified; ticket #118).
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddMinutes(-1));
        await db.Get<IReminderService>().SetNotifyAtDueAsync(task.Id, false, Ct);
        AdvanceToNow(db);
        var tasks = db.Get<ITaskService>();

        await tasks.UpdateTaskAsync(task.Id, "renamed", "notes", Priority.High, Ct);
        await tasks.AddSubtaskAsync(task.Id, "step", Ct);
        await db.Get<IDueService>().SetDueAsync(task.Id, new DateOnly(2026, 10, 20), null, Ct);

        var due = Assert.Single(await db.Get<IReminderService>().ListDueAsync(Ct));
        Assert.Equal(Now.AddMinutes(-1), due.DueAtUtc);
    }

    [Fact]
    public async Task Un_completing_a_Task_counts_its_past_moments_as_notified()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddMinutes(-1));
        var later = await AddAsync(db, "later", Now.AddHours(1));
        var tasks = db.Get<ITaskService>();
        await tasks.SetCompletedAsync(task.Id, true, Ct);
        await tasks.SetCompletedAsync(later.Id, true, Ct);
        AdvanceToNow(db);

        await tasks.SetCompletedAsync(task.Id, false, Ct);
        await tasks.SetCompletedAsync(later.Id, false, Ct);

        Assert.Empty(await DueTitlesAsync(db));
        db.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(["later"], await DueTitlesAsync(db));
    }

    [Fact]
    public async Task Undoing_a_completion_counts_past_moments_as_notified()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddMinutes(-1));
        await db.Get<ITaskService>().SetCompletedAsync(task.Id, true, Ct);
        AdvanceToNow(db);

        await db.Get<IRecurrenceService>().UndoCompletionAsync(task.Id, null, Ct);

        Assert.Empty(await DueTitlesAsync(db));
    }

    [Fact]
    public async Task Creating_a_Task_counts_moments_already_past_as_notified()
    {
        using var db = await StartAsync();
        var list = await db.Get<IListService>().CreateListAsync("Home", Ct);

        var created = await db.Get<ITaskService>().CreateTaskAsync("a", Ct);
        var quickAdded = await db.Get<ITaskCreator>().CreateTaskInListAsync("b", list.Id, new DateOnly(2026, 10, 20), Ct);

        Assert.Equal(Start, (await StoredAsync(db, created.Id)).NotifiedThroughUtc);
        Assert.Equal(Start, (await StoredAsync(db, quickAdded.Id)).NotifiedThroughUtc);
    }

    [Fact]
    public async Task Editing_a_fired_Reminder_re_arms_it()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db);
        var reminders = db.Get<IReminderService>();
        await reminders.TryMarkFiredAsync(task.Id, Now.AddMinutes(-1), Ct);

        var updated = await reminders.UpdateReminderAsync(ReminderId(task), ReminderValue.Fixed(Now.AddMinutes(5)), Ct);
        Assert.True(updated.NotifiedThroughUtc < updated.Reminders[0].AtUtc);
        Assert.Empty(await DueTitlesAsync(db));

        db.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(["a"], await DueTitlesAsync(db));
    }

    [Fact]
    public async Task Setting_the_same_Reminder_again_keeps_it_fired()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db);
        var reminders = db.Get<IReminderService>();
        await reminders.TryMarkFiredAsync(task.Id, Now.AddMinutes(-1), Ct);

        await reminders.UpdateReminderAsync(ReminderId(task), ReminderValue.Fixed(Now.AddMinutes(-1)), Ct);

        Assert.Empty(await DueTitlesAsync(db));
    }

    [Fact]
    public async Task Editing_the_Reminder_clears_the_Snooze()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddHours(-1));
        await SnoozeAsync(db, task.Id, Now.AddHours(1));
        AdvanceToNow(db);

        var updated = await db.Get<IReminderService>().UpdateReminderAsync(
            ReminderId(task), ReminderValue.Fixed(Now.AddMinutes(1)), Ct);

        Assert.Null(updated.SnoozedUntilUtc);
        db.Clock.Advance(TimeSpan.FromMinutes(1));
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
        Assert.Equal(Now.AddHours(-1), Assert.Single(completed.Reminders).AtUtc);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(60)]
    public async Task Snoozing_a_fired_Reminder_stores_the_Snooze_from_now_and_keeps_the_planned_Reminder(int minutes)
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db);
        var reminders = db.Get<IReminderService>();
        await reminders.TryMarkFiredAsync(task.Id, Now.AddMinutes(-1), Ct);

        var snoozed = await reminders.SnoozeAsync(task.Id, TimeSpan.FromMinutes(minutes), Ct);

        var stored = Assert.Single(await db.Get<ITaskService>().ListDefaultListTasksAsync(Ct));
        Assert.Equal(Now.AddMinutes(minutes), snoozed.SnoozedUntilUtc);
        Assert.Equal(Now.AddMinutes(minutes), stored.SnoozedUntilUtc);
        Assert.Equal(Now.AddMinutes(-1), Assert.Single(stored.Reminders).AtUtc);
        Assert.Equal(Now.AddMinutes(-1), stored.NotifiedThroughUtc);
        Assert.Empty(await DueTitlesAsync(db));

        db.Clock.Advance(TimeSpan.FromMinutes(minutes));
        var due = Assert.Single(await reminders.ListDueAsync(Ct));
        Assert.Equal(Now.AddMinutes(minutes), due.DueAtUtc);
    }

    [Fact]
    public async Task Snoozing_a_missing_Task_throws()
    {
        using var db = await StartAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => db.Get<IReminderService>().SnoozeAsync(Guid.NewGuid(), ReminderCalculator.ShortSnooze, Ct));
    }

    [Fact]
    public async Task Snoozing_a_completed_Task_changes_nothing()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db);
        await db.Get<ITaskService>().SetCompletedAsync(task.Id, true, Ct);

        var result = await db.Get<IReminderService>().SnoozeAsync(task.Id, ReminderCalculator.ShortSnooze, Ct);

        Assert.Null(result.SnoozedUntilUtc);
        Assert.Null(Assert.Single(await db.QueryAsync(c => c.Tasks)).SnoozedUntilUtc);
    }

    [Fact]
    public async Task Clearing_a_Snooze_re_arms_the_planned_Reminder()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db);
        var reminders = db.Get<IReminderService>();
        await reminders.TryMarkFiredAsync(task.Id, Now.AddMinutes(-1), Ct);
        await reminders.SnoozeAsync(task.Id, ReminderCalculator.LongSnooze, Ct);

        var cleared = await reminders.ClearSnoozeAsync(task.Id, Ct);

        Assert.Null(cleared.SnoozedUntilUtc);
        Assert.Null(cleared.NotifiedThroughUtc);
        var due = Assert.Single(await reminders.ListDueAsync(Ct));
        Assert.Equal(Now.AddMinutes(-1), due.DueAtUtc);
        Assert.True(await reminders.TryMarkFiredAsync(task.Id, due.DueAtUtc, Ct));
        Assert.Empty(await DueTitlesAsync(db));
    }

    [Fact]
    public async Task Clearing_the_Snooze_of_a_missing_Task_throws()
    {
        using var db = await StartAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => db.Get<IReminderService>().ClearSnoozeAsync(Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task Undoing_a_delete_restores_the_Reminder_with_its_state()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddHours(-1));
        await SnoozeAsync(db, task.Id, Now.AddHours(1));
        AdvanceToNow(db);
        await db.Get<IReminderService>().TryMarkFiredAsync(task.Id, Now.AddHours(1), Ct); // not due: unchanged
        var service = db.Get<ITaskService>();

        var deleted = await service.DeleteTaskAsync(task.Id, Ct);
        var restored = await service.RestoreTaskAsync(deleted, Ct);

        Assert.Equal(Now.AddHours(-1), Assert.Single(restored.Reminders).AtUtc);
        Assert.Equal(Now.AddHours(1), restored.SnoozedUntilUtc);
        Assert.Equal(Start, restored.NotifiedThroughUtc);
        Assert.Equal(Start, (await StoredAsync(db, task.Id)).NotifiedThroughUtc);
    }

    [Fact]
    public async Task Undoing_a_delete_keeps_a_fired_Reminder_fired()
    {
        using var db = await StartAsync();
        var task = await AddDueAsync(db);
        await db.Get<IReminderService>().TryMarkFiredAsync(task.Id, Now.AddMinutes(-1), Ct);
        var service = db.Get<ITaskService>();

        await service.RestoreTaskAsync(await service.DeleteTaskAsync(task.Id, Ct), Ct);

        Assert.Empty(await DueTitlesAsync(db));
    }

    [Fact]
    public async Task Undoing_a_delete_restores_every_Reminder()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Now.AddHours(2));
        var withBoth = await db.Get<IReminderService>().AddReminderAsync(task.Id, ReminderValue.Fixed(Now.AddHours(1)), Ct);
        var service = db.Get<ITaskService>();

        var restored = await service.RestoreTaskAsync(await service.DeleteTaskAsync(task.Id, Ct), Ct);

        Assert.Equal([Now.AddHours(1), Now.AddHours(2)], Moments(restored));
        var stored = await StoredAsync(db, task.Id);
        Assert.Equal(
            withBoth.Reminders.Select(r => (r.Id, r.AtUtc)),
            stored.Reminders.OrderBy(r => r.AtUtc).Select(r => (r.Id, r.AtUtc)));

        AdvanceToNow(db);
        db.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(["a"], await DueTitlesAsync(db));
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

        var withReminder = await db.Get<IReminderService>().AddReminderAsync(task.Id, ReminderValue.Fixed(Now), Ct);
        Assert.Equal(2, raised);

        await db.Get<IReminderService>().UpdateReminderAsync(
            ReminderId(withReminder), ReminderValue.Fixed(Now.AddHours(1)), Ct);
        Assert.Equal(3, raised);

        await db.Get<IReminderService>().RemoveReminderAsync(ReminderId(withReminder), Ct);
        Assert.Equal(4, raised);
    }
}
