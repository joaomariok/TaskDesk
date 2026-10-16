using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public class RecurrenceServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Local time is UTC-3. The clock is moved to local 2026-10-20 10:00 (a Tuesday).
    private static async Task<TempDatabase> StartAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        db.Clock.SetLocalTimeZone(
            TimeZoneInfo.CreateCustomTimeZone("Test-3", TimeSpan.FromHours(-3), "Test-3", "Test-3"));
        db.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        return db;
    }

    private static DateOnly Day(int day) => new(2026, 10, day);

    private static async Task<Guid> DefaultListIdAsync(TempDatabase db) =>
        (await db.Get<IListService>().GetListsAsync(Ct)).Single(l => l.List.IsDefault).List.Id;

    private static async Task<TaskItem> AddAsync(
        TempDatabase db, string title, DateOnly? due, Recurrence? recurrence = null, TimeOnly? time = null)
    {
        var task = await db.Get<ITaskCreator>().CreateTaskInListAsync(title, await DefaultListIdAsync(db), due, Ct);
        if (time is not null)
        {
            task = await db.Get<IDueService>().SetDueAsync(task.Id, due, time, Ct);
        }

        return recurrence is null ? task : await db.Get<IRecurrenceService>().SetRecurrenceAsync(task.Id, recurrence, Ct);
    }

    private static Task<List<TaskItem>> AllTasksAsync(TempDatabase db) =>
        db.QueryAsync(c => c.Tasks.Include(t => t.Tags).Include(t => t.Subtasks).OrderBy(t => t.CreatedUtc).ThenBy(t => t.Id));

    private static async Task<TaskItem> TaskAsync(TempDatabase db, Guid id) =>
        (await AllTasksAsync(db)).Single(t => t.Id == id);

    private static Task<TaskItem> CompleteAsync(TempDatabase db, Guid id, bool completed = true) =>
        db.Get<ITaskService>().SetCompletedAsync(id, completed, Ct);

    // ---- Setting a Recurrence ----

    [Fact]
    public async Task A_Recurrence_is_stored_and_read_back_with_every_field()
    {
        using var db = await StartAsync();
        var weekly = Recurrence.Weekly(RecurrenceDays.Monday | RecurrenceDays.Friday, 2) with
        {
            Anchor = RecurrenceAnchor.CompletionDate,
            EndDate = new DateOnly(2027, 3, 1),
        };
        var yearly = Recurrence.Yearly(2, 29);
        var a = await AddAsync(db, "a", Day(23), weekly);
        var b = await AddAsync(db, "b", Day(29), yearly);

        Assert.Equal(weekly, (await TaskAsync(db, a.Id)).Recurrence);
        Assert.Equal(yearly, (await TaskAsync(db, b.Id)).Recurrence);
    }

    [Fact]
    public async Task A_Recurrence_can_be_cleared()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20), Recurrence.Daily());

        await db.Get<IRecurrenceService>().SetRecurrenceAsync(task.Id, null, Ct);

        Assert.Null((await TaskAsync(db, task.Id)).Recurrence);
    }

    [Fact]
    public async Task A_Recurrence_needs_a_Due_date()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", null);

        await Assert.ThrowsAsync<ArgumentException>(
            () => db.Get<IRecurrenceService>().SetRecurrenceAsync(task.Id, Recurrence.Daily(), Ct));

        Assert.Null((await TaskAsync(db, task.Id)).Recurrence);
    }

    [Fact]
    public async Task An_invalid_Recurrence_is_rejected()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20));

        await Assert.ThrowsAsync<ArgumentException>(() => db.Get<IRecurrenceService>().SetRecurrenceAsync(
            task.Id, Recurrence.Weekly(RecurrenceDays.None), Ct));

        Assert.Null((await TaskAsync(db, task.Id)).Recurrence);
    }

    [Fact]
    public async Task The_Due_date_of_a_recurring_Task_cannot_be_cleared()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20), Recurrence.Daily());

        await Assert.ThrowsAsync<ArgumentException>(
            () => db.Get<IDueService>().SetDueAsync(task.Id, null, null, Ct));

        Assert.Equal(Day(20), (await TaskAsync(db, task.Id)).DueDate);
    }

    // ---- Completing an Occurrence ----

    [Fact]
    public async Task Completing_an_Occurrence_creates_the_next_one_as_a_copy()
    {
        using var db = await StartAsync();
        var recurrence = Recurrence.Weekly(RecurrenceDays.Tuesday | RecurrenceDays.Thursday);
        var task = await AddAsync(db, "Water plants", Day(20), recurrence, new TimeOnly(8, 15));
        await db.Get<ITaskService>().UpdateTaskAsync(task.Id, "Water plants", "Balcony too", Priority.High, Ct);
        await db.Get<ITagService>().AddTagToTaskAsync(task.Id, "home", Ct);
        var ticked = await db.Get<ITaskService>().AddSubtaskAsync(task.Id, "fern", Ct);
        await db.Get<ITaskService>().AddSubtaskAsync(task.Id, "cactus", Ct);
        await db.Get<ITaskService>().SetSubtaskCompletedAsync(ticked.Id, true, Ct);

        var completed = await CompleteAsync(db, task.Id);

        var tasks = await AllTasksAsync(db);
        Assert.Equal(2, tasks.Count);
        var old = tasks.Single(t => t.Id == task.Id);
        var next = tasks.Single(t => t.Id != task.Id);
        Assert.True(old.IsCompleted);
        Assert.Equal(next.Id, old.NextOccurrenceId);
        Assert.Equal(next.Id, completed.NextOccurrenceId);
        Assert.NotNull(old.SeriesId);
        Assert.Equal(old.SeriesId, next.SeriesId);

        Assert.False(next.IsCompleted);
        Assert.Null(next.CompletedUtc);
        Assert.Null(next.NextOccurrenceId);
        Assert.Equal(old.ListId, next.ListId);
        Assert.Equal("Water plants", next.Title);
        Assert.Equal("Balcony too", next.Notes);
        Assert.Equal(Priority.High, next.Priority);
        Assert.Equal(["home"], next.Tags.Select(t => t.Name));
        Assert.Equal(recurrence, next.Recurrence);
        Assert.Equal(Day(22), next.DueDate);
        Assert.Equal(new TimeOnly(8, 15), next.DueTime);

        // Subtasks are copied unticked; the completed Occurrence keeps its own Subtasks as they were.
        Assert.Equal(
            [("fern", false), ("cactus", false)],
            next.Subtasks.OrderBy(s => s.SortOrder).Select(s => (s.Title, s.IsCompleted)));
        Assert.DoesNotContain(next.Subtasks, s => old.Subtasks.Exists(o => o.Id == s.Id));
        Assert.Equal(
            [("fern", true), ("cactus", false)],
            old.Subtasks.OrderBy(s => s.SortOrder).Select(s => (s.Title, s.IsCompleted)));
    }

    // ---- The Reminder of the next Occurrence ----

    private static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public async Task The_next_Occurrence_keeps_the_planned_Reminder_offset_never_the_Snooze()
    {
        using var db = await StartAsync();
        // Due 20 Oct 14:00 local (17:00 UTC); Reminder an hour before, at 13:00 local (16:00 UTC).
        var task = await AddAsync(db, "a", Day(20), Recurrence.Daily(), new TimeOnly(14, 0));
        var reminders = db.Get<IReminderService>();
        await reminders.SetReminderAsync(task.Id, Utc(20, 16), Ct);
        db.Clock.SetUtcNow(new DateTimeOffset(Utc(20, 16, 1)));
        Assert.True(await reminders.TryMarkFiredAsync(task.Id, Utc(20, 16), Ct));
        await reminders.SnoozeAsync(task.Id, ReminderCalculator.LongSnooze, Ct);

        await CompleteAsync(db, task.Id);

        var old = await TaskAsync(db, task.Id);
        var next = await TaskAsync(db, old.NextOccurrenceId!.Value);
        Assert.Equal(Utc(20, 16), old.ReminderUtc);
        Assert.Null(old.SnoozedUntilUtc);
        Assert.Equal(Utc(21, 16), next.ReminderUtc);
        Assert.Null(next.SnoozedUntilUtc);
        Assert.Null(next.ReminderFiredUtc);
    }

    [Fact]
    public async Task A_date_only_Due_measures_the_Reminder_offset_from_local_midnight_across_daylight_saving()
    {
        using var db = await StartAsync();
        // UTC+1, UTC+2 in daylight saving, which ends on 25 October 2026 at 03:00 local.
        db.Clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone(
            "Test+1-DST", TimeSpan.FromHours(1), "Test+1-DST", "Test+1", "Test+2",
            [
                TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                    DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
                    TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
                    TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday)),
            ]));
        var task = await AddAsync(db, "a", Day(24), Recurrence.Daily());
        await db.Get<IReminderService>().SetReminderAsync(task.Id, Utc(24, 7), Ct); // 09:00 local, daylight saving

        await CompleteAsync(db, task.Id);

        var next = await TaskAsync(db, (await TaskAsync(db, task.Id)).NextOccurrenceId!.Value);
        Assert.Equal(Day(25), next.DueDate);
        Assert.Equal(Utc(25, 8), next.ReminderUtc); // still 09:00 local, now standard time
    }

    [Fact]
    public async Task An_Occurrence_without_a_Reminder_produces_one_without_a_Reminder()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20), Recurrence.Daily());

        await CompleteAsync(db, task.Id);

        var next = await TaskAsync(db, (await TaskAsync(db, task.Id)).NextOccurrenceId!.Value);
        Assert.Null(next.ReminderUtc);
    }

    [Fact]
    public async Task A_Skip_carries_the_planned_Reminder_to_the_next_Occurrence()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20), Recurrence.Daily());
        await db.Get<IReminderService>().SetReminderAsync(task.Id, Utc(19, 21), Ct); // 18:00 local the day before

        var skipped = await db.Get<IRecurrenceService>().SkipOccurrenceAsync(task.Id, Ct);

        Assert.Equal(Utc(20, 21), (await TaskAsync(db, skipped.ProducedOccurrenceId!.Value)).ReminderUtc);
    }

    [Fact]
    public async Task The_next_Occurrence_is_placed_right_after_the_completed_one()
    {
        using var db = await StartAsync();
        var a = await AddAsync(db, "a", Day(20), Recurrence.Daily());
        await AddAsync(db, "b", null);

        await CompleteAsync(db, a.Id);

        var ordered = await db.Get<ITaskService>().ListDefaultListTasksAsync(Ct);
        Assert.Equal(["a", "a", "b"], ordered.Select(t => t.Title));
        Assert.Equal(a.Id, ordered[0].Id);
    }

    [Fact]
    public async Task Later_Occurrences_keep_the_same_Series()
    {
        using var db = await StartAsync();
        var first = await AddAsync(db, "a", Day(20), Recurrence.Daily());

        var second = (await CompleteAsync(db, first.Id)).NextOccurrenceId!.Value;
        var third = (await CompleteAsync(db, second)).NextOccurrenceId!.Value;

        var tasks = await AllTasksAsync(db);
        Assert.Single(tasks.Select(t => t.SeriesId).Distinct());
        Assert.Equal(Day(22), tasks.Single(t => t.Id == third).DueDate);
    }

    [Fact]
    public async Task An_Occurrence_produces_its_next_Occurrence_at_most_once()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20), Recurrence.Daily());

        var first = await CompleteAsync(db, task.Id);
        await CompleteAsync(db, task.Id, completed: false);
        var again = await CompleteAsync(db, task.Id);

        Assert.Equal(2, (await AllTasksAsync(db)).Count);
        Assert.Equal(first.NextOccurrenceId, again.NextOccurrenceId);
    }

    [Fact]
    public async Task A_manual_un_complete_keeps_the_next_Occurrence()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20), Recurrence.Daily());
        var nextId = (await CompleteAsync(db, task.Id)).NextOccurrenceId;

        var reopened = await CompleteAsync(db, task.Id, completed: false);

        Assert.False(reopened.IsCompleted);
        Assert.Equal(nextId, reopened.NextOccurrenceId);
        Assert.Contains(await AllTasksAsync(db), t => t.Id == nextId);
    }

    [Fact]
    public async Task Completing_after_the_end_date_creates_no_next_Occurrence()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20), Recurrence.Daily() with { EndDate = Day(20) });

        var completed = await CompleteAsync(db, task.Id);

        Assert.True(completed.IsCompleted);
        Assert.Null(completed.NextOccurrenceId);
        Assert.Single(await AllTasksAsync(db));
    }

    [Fact]
    public async Task A_completion_date_Anchor_counts_from_the_local_completion_date()
    {
        using var db = await StartAsync();
        var task = await AddAsync(
            db, "a", Day(15), Recurrence.Daily(3) with { Anchor = RecurrenceAnchor.CompletionDate });
        // 01:00 UTC on Oct 21 is still Oct 20 in the local zone (UTC-3).
        db.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 21, 1, 0, 0, TimeSpan.Zero));

        var nextId = (await CompleteAsync(db, task.Id)).NextOccurrenceId!.Value;

        Assert.Equal(Day(23), (await TaskAsync(db, nextId)).DueDate);
    }

    [Fact]
    public async Task Completing_a_Task_without_a_Recurrence_creates_nothing()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20));

        var completed = await CompleteAsync(db, task.Id);

        Assert.Null(completed.NextOccurrenceId);
        Assert.Null(completed.SeriesId);
        Assert.Single(await AllTasksAsync(db));
    }

    [Fact]
    public async Task The_completion_transaction_rolls_back_fully_on_a_failure()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20), Recurrence.Daily());
        await db.Get<ITaskService>().AddSubtaskAsync(task.Id, "step", Ct);
        // Inserting the copied Subtask fails after the completion and the new Task were written.
        await using (var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(Ct))
        {
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER fail_subtask BEFORE INSERT ON Subtasks BEGIN SELECT RAISE(ABORT, 'simulated'); END;",
                Ct);
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => CompleteAsync(db, task.Id));

        var tasks = await AllTasksAsync(db);
        var stored = Assert.Single(tasks);
        Assert.False(stored.IsCompleted);
        Assert.Null(stored.CompletedUtc);
        Assert.Null(stored.NextOccurrenceId);
        Assert.Null(stored.SeriesId);
        Assert.Single(stored.Subtasks);
    }

    // ---- Undo of a completion (Ctrl+Z) ----

    [Fact]
    public async Task Undoing_a_completion_deletes_the_Occurrence_it_produced()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20), Recurrence.Daily());
        var produced = (await CompleteAsync(db, task.Id)).NextOccurrenceId;

        var undone = await db.Get<IRecurrenceService>().UndoCompletionAsync(task.Id, produced, Ct);

        Assert.False(undone.IsCompleted);
        Assert.Null(undone.NextOccurrenceId);
        var stored = Assert.Single(await AllTasksAsync(db));
        Assert.False(stored.IsCompleted);
        Assert.Null(stored.CompletedUtc);
        Assert.Null(stored.NextOccurrenceId);
    }

    [Fact]
    public async Task After_an_undone_completion_completing_again_produces_one_next_Occurrence()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20), Recurrence.Daily());
        var produced = (await CompleteAsync(db, task.Id)).NextOccurrenceId;
        await db.Get<IRecurrenceService>().UndoCompletionAsync(task.Id, produced, Ct);

        var again = await CompleteAsync(db, task.Id);

        Assert.NotNull(again.NextOccurrenceId);
        Assert.Equal(2, (await AllTasksAsync(db)).Count);
    }

    [Fact]
    public async Task Undoing_a_completion_that_produced_nothing_keeps_the_existing_next_Occurrence()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20), Recurrence.Daily());
        var nextId = (await CompleteAsync(db, task.Id)).NextOccurrenceId;
        await CompleteAsync(db, task.Id, completed: false);
        await CompleteAsync(db, task.Id); // produces nothing: the next Occurrence exists already

        var undone = await db.Get<IRecurrenceService>().UndoCompletionAsync(task.Id, null, Ct);

        Assert.False(undone.IsCompleted);
        Assert.Equal(nextId, undone.NextOccurrenceId);
        Assert.Equal(2, (await AllTasksAsync(db)).Count);
    }

    // ---- Skip and Stop repeating ----

    [Fact]
    public async Task Skipping_deletes_the_Occurrence_and_still_produces_the_next_one()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(19), Recurrence.Daily(2));
        await db.Get<ITagService>().AddTagToTaskAsync(task.Id, "home", Ct);

        var skipped = await db.Get<IRecurrenceService>().SkipOccurrenceAsync(task.Id, Ct);

        var next = Assert.Single(await AllTasksAsync(db));
        Assert.Equal(skipped.ProducedOccurrenceId, next.Id);
        Assert.Equal(Day(21), next.DueDate);
        Assert.Equal(["home"], next.Tags.Select(t => t.Name));
        Assert.Equal(task.Id, skipped.Deleted.Task.Id);
        Assert.NotNull(next.SeriesId);
    }

    [Fact]
    public async Task A_Skip_uses_the_Skip_date_as_the_completion_date()
    {
        using var db = await StartAsync();
        var task = await AddAsync(
            db, "a", Day(15), Recurrence.Daily(3) with { Anchor = RecurrenceAnchor.CompletionDate });

        await db.Get<IRecurrenceService>().SkipOccurrenceAsync(task.Id, Ct);

        Assert.Equal(Day(23), Assert.Single(await AllTasksAsync(db)).DueDate);
    }

    [Fact]
    public async Task Skipping_an_Occurrence_that_already_produced_its_next_one_produces_no_other()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20), Recurrence.Daily());
        var nextId = (await CompleteAsync(db, task.Id)).NextOccurrenceId;
        await CompleteAsync(db, task.Id, completed: false);

        var skipped = await db.Get<IRecurrenceService>().SkipOccurrenceAsync(task.Id, Ct);

        Assert.Null(skipped.ProducedOccurrenceId);
        Assert.Equal(nextId, Assert.Single(await AllTasksAsync(db)).Id);
    }

    [Fact]
    public async Task Skipping_a_Task_without_a_Recurrence_is_rejected()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.Get<IRecurrenceService>().SkipOccurrenceAsync(task.Id, Ct));

        Assert.Single(await AllTasksAsync(db));
    }

    [Fact]
    public async Task Undoing_a_Skip_restores_the_Occurrence_and_deletes_the_one_it_produced()
    {
        using var db = await StartAsync();
        var recurrence = Recurrence.Daily();
        var task = await AddAsync(db, "a", Day(20), recurrence);
        await db.Get<ITaskService>().AddSubtaskAsync(task.Id, "step", Ct);
        await db.Get<ITagService>().AddTagToTaskAsync(task.Id, "home", Ct);
        var skipped = await db.Get<IRecurrenceService>().SkipOccurrenceAsync(task.Id, Ct);

        var restored = await db.Get<IRecurrenceService>().UndoSkipAsync(skipped, Ct);

        Assert.Equal(task.Id, restored.Id);
        var stored = Assert.Single(await AllTasksAsync(db));
        Assert.Equal(task.Id, stored.Id);
        Assert.Equal(recurrence, stored.Recurrence);
        Assert.Equal(skipped.Deleted.Task.SeriesId, stored.SeriesId);
        Assert.Null(stored.NextOccurrenceId);
        Assert.Equal(["step"], stored.Subtasks.Select(s => s.Title));
        Assert.Equal(["home"], stored.Tags.Select(t => t.Name));
    }

    [Fact]
    public async Task Undoing_a_Skip_that_produced_nothing_keeps_the_existing_next_Occurrence()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(20), Recurrence.Daily());
        var nextId = (await CompleteAsync(db, task.Id)).NextOccurrenceId;
        await CompleteAsync(db, task.Id, completed: false);
        var skipped = await db.Get<IRecurrenceService>().SkipOccurrenceAsync(task.Id, Ct);

        var restored = await db.Get<IRecurrenceService>().UndoSkipAsync(skipped, Ct);

        Assert.Equal(nextId, restored.NextOccurrenceId);
        Assert.Equal(2, (await AllTasksAsync(db)).Count);
    }

    [Fact]
    public async Task Stop_repeating_deletes_the_Occurrence_without_a_next_one_and_undo_restores_its_Recurrence()
    {
        using var db = await StartAsync();
        var recurrence = Recurrence.Monthly(20);
        var task = await AddAsync(db, "a", Day(20), recurrence);
        var nextId = (await CompleteAsync(db, task.Id)).NextOccurrenceId!.Value;
        var series = (await TaskAsync(db, nextId)).SeriesId;

        var deleted = await db.Get<ITaskService>().DeleteTaskAsync(nextId, Ct);

        Assert.Equal([task.Id], (await AllTasksAsync(db)).Select(t => t.Id));

        await db.Get<ITaskService>().RestoreTaskAsync(deleted, Ct);

        var restored = await TaskAsync(db, nextId);
        Assert.Equal(recurrence, restored.Recurrence);
        Assert.Equal(series, restored.SeriesId);
        Assert.Equal(2, (await AllTasksAsync(db)).Count);
    }
}
