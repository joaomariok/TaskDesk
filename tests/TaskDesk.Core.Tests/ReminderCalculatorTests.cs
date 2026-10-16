using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public class ReminderCalculatorTests
{
    private static readonly DateTime Planned = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static FakeTimeProvider ClockAtUtc(int hour, int minute, int second = 0) =>
        new(new DateTimeOffset(2026, 10, 20, hour, minute, second, TimeSpan.Zero));

    private static DateTime Utc(int hour, int minute) => new(2026, 10, 20, hour, minute, 0, DateTimeKind.Utc);

    private static TaskItem Task(
        DateTime? reminder = null, DateTime? snoozed = null, DateTime? fired = null, bool completed = false) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Title = "Task",
            ReminderUtc = reminder,
            SnoozedUntilUtc = snoozed,
            ReminderFiredUtc = fired,
            IsCompleted = completed,
        };

    public static TheoryData<string, int, int, int, bool> DueCases => new()
    {
        // description, now (hour, minute, second UTC on the Reminder's day), expected; the Reminder is at 12:00
        { "a second before the Reminder", 11, 59, 59, false },
        { "exactly at the Reminder", 12, 0, 0, true },
        { "a second after the Reminder", 12, 0, 1, true },
        { "hours after the Reminder (missed)", 18, 0, 0, true },
    };

    [Theory]
    [MemberData(nameof(DueCases))]
    public void A_Reminder_is_due_from_its_instant_on(string description, int hour, int minute, int second, bool expected)
    {
        var clock = ClockAtUtc(hour, minute, second);

        Assert.True(
            expected == ReminderCalculator.IsDue(Task(Planned), clock.GetUtcNow()), description);
    }

    [Fact]
    public void A_Task_without_a_Reminder_is_never_due()
    {
        Assert.False(ReminderCalculator.IsDue(Task(), ClockAtUtc(23, 0).GetUtcNow()));
    }

    [Fact]
    public void An_already_fired_Reminder_is_not_due()
    {
        var task = Task(Planned, fired: Utc(12, 0));

        Assert.False(ReminderCalculator.IsDue(task, ClockAtUtc(13, 0).GetUtcNow()));
    }

    [Fact]
    public void The_Reminder_of_a_completed_Task_is_not_due()
    {
        var task = Task(Planned, completed: true);

        Assert.False(ReminderCalculator.IsDue(task, ClockAtUtc(13, 0).GetUtcNow()));
    }

    [Fact]
    public void A_Snooze_replaces_the_planned_Reminder_as_the_instant_it_fires()
    {
        var task = Task(Planned, snoozed: Utc(12, 10));

        Assert.False(ReminderCalculator.IsDue(task, ClockAtUtc(12, 5).GetUtcNow()));
        Assert.True(ReminderCalculator.IsDue(task, ClockAtUtc(12, 10).GetUtcNow()));
        Assert.Equal(Utc(12, 10), ReminderCalculator.EffectiveUtc(task));
    }

    [Fact]
    public void SelectDue_keeps_only_due_Reminders_oldest_first()
    {
        var late = Task(Utc(11, 0));
        var early = Task(Utc(9, 0));
        var future = Task(Utc(15, 0));
        var fired = Task(Utc(8, 0), fired: Utc(8, 0));
        var completed = Task(Utc(8, 0), completed: true);
        var none = Task();

        var due = ReminderCalculator.SelectDue([late, future, fired, early, completed, none], ClockAtUtc(12, 0).GetUtcNow());

        Assert.Equal([early.Id, late.Id], due.Select(t => t.Id));
    }

    // 3 hours behind UTC, without daylight saving.
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test-3", TimeSpan.FromHours(-3), "Test-3", "Test-3");

    // UTC+1 with daylight saving (UTC+2) from the last Sunday of March 02:00 to the last Sunday of October 03:00.
    private static readonly TimeZoneInfo DaylightZone = TimeZoneInfo.CreateCustomTimeZone(
        "Test+1-DST", TimeSpan.FromHours(1), "Test+1-DST", "Test+1", "Test+2",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday)),
        ]);

    [Fact]
    public void A_local_date_and_time_become_a_UTC_Reminder_instant()
    {
        var utc = ReminderCalculator.ToUtc(new DateOnly(2026, 10, 20), new TimeOnly(14, 30), Zone);

        Assert.Equal(new DateTime(2026, 10, 20, 17, 30, 0, DateTimeKind.Utc), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void A_date_only_Reminder_fires_at_the_default_time_of_09_00()
    {
        var utc = ReminderCalculator.ToUtc(new DateOnly(2026, 10, 20), null, Zone);

        Assert.Equal(new TimeOnly(9, 0), ReminderCalculator.DefaultReminderTime);
        Assert.Equal(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void A_Reminder_instant_shows_as_its_local_date_and_time()
    {
        var (date, time) = ReminderCalculator.ToLocal(new DateTime(2026, 10, 21, 1, 15, 0, DateTimeKind.Utc), Zone);

        Assert.Equal(new DateOnly(2026, 10, 20), date);
        Assert.Equal(new TimeOnly(22, 15), time);
    }

    [Fact]
    public void A_local_time_skipped_by_daylight_saving_moves_forward_by_the_gap()
    {
        // 29 March 2026: clocks jump from 02:00 to 03:00, so 02:30 does not exist; it means 03:30 (01:30 UTC).
        var utc = ReminderCalculator.ToUtc(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), DaylightZone);

        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void A_repeated_local_time_at_the_end_of_daylight_saving_takes_its_first_occurrence()
    {
        // 25 October 2026: clocks go back from 03:00 to 02:00, so 02:30 happens twice; the first is 00:30 UTC.
        var utc = ReminderCalculator.ToUtc(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), DaylightZone);

        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void Editing_a_fired_Reminder_re_arms_it()
    {
        var task = Task(Planned, snoozed: Utc(12, 10), fired: Utc(12, 10));

        var changed = ReminderCalculator.SetPlanned(task, Utc(15, 0));

        Assert.True(changed);
        Assert.Equal(Utc(15, 0), task.ReminderUtc);
        Assert.Null(task.ReminderFiredUtc);
        Assert.Null(task.SnoozedUntilUtc);
    }

    [Fact]
    public void Setting_the_same_Reminder_again_changes_nothing()
    {
        var task = Task(Planned, fired: Utc(12, 0));

        var changed = ReminderCalculator.SetPlanned(task, Planned);

        Assert.False(changed);
        Assert.Equal(Utc(12, 0), task.ReminderFiredUtc);
    }

    [Fact]
    public void Clearing_a_Reminder_clears_its_Snooze_and_fired_marker()
    {
        var task = Task(Planned, snoozed: Utc(12, 10), fired: Utc(12, 0));

        Assert.True(ReminderCalculator.SetPlanned(task, null));

        Assert.Null(task.ReminderUtc);
        Assert.Null(task.SnoozedUntilUtc);
        Assert.Null(task.ReminderFiredUtc);
    }
    // ---- Snooze ----

    public static TheoryData<string, int> SnoozeCases => new()
    {
        // description, Snooze duration in minutes
        { "Snooze 10 min", 10 },
        { "Snooze 1 h", 60 },
    };

    [Theory]
    [MemberData(nameof(SnoozeCases))]
    public void Snoozing_a_fired_Reminder_sets_the_Snooze_from_now_and_leaves_the_planned_Reminder(
        string description, int minutes)
    {
        var task = Task(Planned, fired: Utc(12, 0));
        var clock = ClockAtUtc(12, 3);

        Assert.True(ReminderCalculator.Snooze(task, clock.GetUtcNow(), TimeSpan.FromMinutes(minutes)), description);

        Assert.Equal(Utc(12, 3).AddMinutes(minutes), task.SnoozedUntilUtc);
        Assert.Equal(Planned, task.ReminderUtc);
        Assert.Null(task.ReminderFiredUtc);
        Assert.Equal(Utc(12, 3).AddMinutes(minutes), ReminderCalculator.EffectiveUtc(task));
        Assert.False(ReminderCalculator.IsDue(task, clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromMinutes(minutes));
        Assert.True(ReminderCalculator.IsDue(task, clock.GetUtcNow()));
    }

    [Fact]
    public void The_Snooze_durations_are_10_minutes_and_1_hour()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), ReminderCalculator.ShortSnooze);
        Assert.Equal(TimeSpan.FromHours(1), ReminderCalculator.LongSnooze);
    }

    [Fact]
    public void A_completed_Task_cannot_be_snoozed()
    {
        var task = Task(Planned, fired: Utc(12, 0), completed: true);

        Assert.False(ReminderCalculator.Snooze(task, ClockAtUtc(12, 3).GetUtcNow(), ReminderCalculator.ShortSnooze));

        Assert.Null(task.SnoozedUntilUtc);
        Assert.Equal(Utc(12, 0), task.ReminderFiredUtc);
    }

    [Fact]
    public void Clearing_a_Snooze_re_arms_the_planned_Reminder()
    {
        var task = Task(Planned, snoozed: Utc(13, 0), fired: Utc(13, 0));

        Assert.True(ReminderCalculator.ClearSnooze(task));

        Assert.Null(task.SnoozedUntilUtc);
        Assert.Null(task.ReminderFiredUtc);
        Assert.Equal(Planned, task.ReminderUtc);
        Assert.True(ReminderCalculator.IsDue(task, ClockAtUtc(13, 5).GetUtcNow()));
    }

    [Fact]
    public void Clearing_when_there_is_no_Snooze_changes_nothing()
    {
        var task = Task(Planned, fired: Utc(12, 0));

        Assert.False(ReminderCalculator.ClearSnooze(task));

        Assert.Equal(Utc(12, 0), task.ReminderFiredUtc);
    }

    // ---- Missed summary ----

    public static TheoryData<int, bool> SummaryCases => new()
    {
        // Reminders due at once, expected: one summary instead of one notification each
        { 0, false },
        { 1, false },
        { 3, false },
        { 4, true },
        { 12, true },
    };

    [Theory]
    [MemberData(nameof(SummaryCases))]
    public void More_than_3_Reminders_due_at_once_are_summarized(int count, bool expected)
    {
        Assert.Equal(expected, ReminderCalculator.ShouldSummarize(count));
    }
}
