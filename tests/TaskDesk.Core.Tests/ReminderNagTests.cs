using System.Globalization;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public class ReminderNagTests
{
    // UTC+1 with daylight saving (UTC+2) from the last Sunday of March 02:00 to the last Sunday of October 03:00.
    private static readonly TimeZoneInfo DaylightZone = TimeZoneInfo.CreateCustomTimeZone(
        "Test+1-DST", TimeSpan.FromHours(1), "Test+1-DST", "Test+1", "Test+2",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday)),
        ]);

    private static readonly MomentRules Rules = new(DaylightZone, new TimeOnly(9, 0));

    // Monday 2026-06-15, in summer time (UTC+2).
    private static readonly DateOnly Monday = new(2026, 6, 15);

    private static DateTimeOffset UtcAt(int day, int hour, int minute = 0) =>
        new(2026, 6, day, hour, minute, 0, TimeSpan.Zero);

    private static DateTime UtcTime(int day, int hour, int minute = 0) =>
        new(2026, 6, day, hour, minute, 0, DateTimeKind.Utc);

    private static DateTime Utc(string value) =>
        DateTime.SpecifyKind(DateTime.Parse(value, CultureInfo.InvariantCulture), DateTimeKind.Utc);

    private static TaskItem NaggingTask(DateOnly? date, TimeOnly? time, NagPeriod? period) => new()
    {
        Id = Guid.CreateVersion7(),
        Title = "Task",
        DueDate = date,
        DueTime = time,
        NotifyAtDue = false, // only the Nag notifies, so the due instant is the latest Nag moment
        NagPeriod = period,
    };

    public static TheoryData<string, string, string?, NagPeriod, string, string?> NagCases => new()
    {
        // description, Due date, Due time (null: date-only), period, now (UTC), expected latest Nag moment (UTC)
        { "no Nag before one period after the Due", "2026-06-15", "14:00", NagPeriod.OneHour, "2026-06-15T12:59", null },
        { "the first Nag one period after the Due", "2026-06-15", "14:00", NagPeriod.OneHour, "2026-06-15T13:00", "2026-06-15T13:00" },
        { "hourly: 15:00, 16:00 and so on", "2026-06-15", "14:00", NagPeriod.OneHour, "2026-06-15T15:30", "2026-06-15T15:00" },
        { "every 15 min", "2026-06-15", "14:00", NagPeriod.FifteenMinutes, "2026-06-15T12:20", "2026-06-15T12:15" },
        { "every 30 min", "2026-06-15", "14:00", NagPeriod.ThirtyMinutes, "2026-06-15T13:10", "2026-06-15T13:00" },
        { "every 3 hours", "2026-06-15", "14:00", NagPeriod.ThreeHours, "2026-06-15T17:59", "2026-06-15T15:00" },
        { "a date-only Due counts from the default Reminder time", "2026-06-15", null, NagPeriod.ThreeHours, "2026-06-15T10:00", "2026-06-15T10:00" },
        { "daily on a date-only Due: not before Tuesday 09:00", "2026-06-15", null, NagPeriod.OneDay, "2026-06-16T06:59", null },
        { "daily on a date-only Due: Tuesday 09:00", "2026-06-15", null, NagPeriod.OneDay, "2026-06-16T07:00", "2026-06-16T07:00" },
        { "daily on a date-only Due: Thursday 09:00", "2026-06-15", null, NagPeriod.OneDay, "2026-06-18T10:00", "2026-06-18T07:00" },
        // Across the spring jump (Sunday 2026-03-29, 02:00 becomes 03:00).
        { "daily across the spring jump keeps the local time", "2026-03-28", "14:00", NagPeriod.OneDay, "2026-03-29T12:00", "2026-03-29T12:00" },
        { "daily across the spring jump: not an hour late", "2026-03-28", "14:00", NagPeriod.OneDay, "2026-03-29T11:59", null },
        { "daily into the skipped hour moves forward by the gap", "2026-03-28", "02:30", NagPeriod.OneDay, "2026-03-29T01:30", "2026-03-29T01:30" },
        { "3 hours across the spring jump count elapsed time", "2026-03-29", "01:30", NagPeriod.ThreeHours, "2026-03-29T03:30", "2026-03-29T03:30" },
        // Across the autumn fall-back (Sunday 2026-10-25, 03:00 becomes 02:00 again).
        { "daily across the fall-back keeps the local time", "2026-10-24", "14:00", NagPeriod.OneDay, "2026-10-25T13:00", "2026-10-25T13:00" },
        { "daily across the fall-back: not an hour early", "2026-10-24", "14:00", NagPeriod.OneDay, "2026-10-25T12:30", null },
        { "3 hours across the fall-back count elapsed time", "2026-10-25", "01:30", NagPeriod.ThreeHours, "2026-10-25T02:30", "2026-10-25T02:30" },
    };

    [Theory]
    [MemberData(nameof(NagCases))]
    public void The_Nag_moments_are_the_Due_moment_plus_whole_periods(
        string description, string dueDate, string? dueTime, NagPeriod period, string now, string? expected)
    {
        var task = NaggingTask(
            DateOnly.Parse(dueDate, CultureInfo.InvariantCulture),
            dueTime is null ? null : TimeOnly.Parse(dueTime, CultureInfo.InvariantCulture),
            period);

        var due = ReminderCalculator.DueInstant(task, new DateTimeOffset(Utc(now)), Rules);

        Assert.True((expected is null ? null : Utc(expected)) == due, $"{description}: {due:O}");
    }

    [Fact]
    public void A_Task_has_no_Nag_by_default()
    {
        Assert.Null(new TaskItem().NagPeriod);
    }

    [Fact]
    public void Without_a_Nag_nothing_notifies_after_the_Due()
    {
        var task = NaggingTask(Monday, new TimeOnly(14, 0), null);

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(20, 0), Rules));
    }

    [Fact]
    public void A_completed_Task_never_Nags()
    {
        var task = NaggingTask(Monday, new TimeOnly(14, 0), NagPeriod.OneHour);
        task.IsCompleted = true;

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(15, 20), Rules));
    }

    [Fact]
    public void After_the_PC_was_off_one_Nag_fires_and_the_schedule_continues_from_the_Due()
    {
        // A Due of 10:00 local (08:00 UTC), notified at the Due; the PC is off from 10:30 to 15:20 local.
        var task = NaggingTask(Monday, new TimeOnly(10, 0), NagPeriod.OneHour);
        task.NotifiedThroughUtc = UtcTime(15, 8);

        var wake = ReminderCalculator.DueInstant(task, UtcAt(15, 13, 20), Rules);
        Assert.Equal(UtcTime(15, 13), wake);
        task.NotifiedThroughUtc = wake; // claimed

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(15, 13, 59), Rules));
        Assert.Equal(UtcTime(15, 14), ReminderCalculator.DueInstant(task, UtcAt(15, 14), Rules));
    }

    [Fact]
    public void A_Snooze_absorbs_Nags_and_the_schedule_resumes()
    {
        // An hourly Nag from a 14:00 local Due (12:00 UTC), snoozed at 15:20 local for 1 h.
        var task = NaggingTask(Monday, new TimeOnly(14, 0), NagPeriod.OneHour);
        task.NotifiedThroughUtc = UtcTime(15, 13);
        ReminderCalculator.Snooze(task, UtcAt(15, 13, 20), ReminderCalculator.LongSnooze);

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(15, 14, 19), Rules));
        var end = ReminderCalculator.DueInstant(task, UtcAt(15, 14, 20), Rules);
        Assert.Equal(UtcTime(15, 14, 20), end);
        task.NotifiedThroughUtc = end; // claimed

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(15, 14, 59), Rules));
        Assert.Equal(UtcTime(15, 15), ReminderCalculator.DueInstant(task, UtcAt(15, 15), Rules));
    }

    [Fact]
    public void Setting_a_Nag_counts_moments_already_past_as_notified()
    {
        var task = NaggingTask(Monday, new TimeOnly(14, 0), null);

        Assert.True(ReminderCalculator.SetNagPeriod(task, NagPeriod.OneHour, UtcAt(15, 15, 30)));

        Assert.Equal(NagPeriod.OneHour, task.NagPeriod);
        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(15, 15, 59), Rules));
        Assert.Equal(UtcTime(15, 16), ReminderCalculator.DueInstant(task, UtcAt(15, 16), Rules));
    }

    [Fact]
    public void Picking_Off_stops_the_Nag()
    {
        var task = NaggingTask(Monday, new TimeOnly(14, 0), NagPeriod.OneHour);
        task.NotifiedThroughUtc = UtcTime(15, 13);

        Assert.True(ReminderCalculator.SetNagPeriod(task, null, UtcAt(15, 13, 30)));

        Assert.Null(task.NagPeriod);
        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(16, 0), Rules));
    }

    [Fact]
    public void Setting_the_same_Nag_changes_nothing()
    {
        var task = NaggingTask(Monday, new TimeOnly(14, 0), NagPeriod.OneHour);
        task.NotifiedThroughUtc = UtcTime(15, 13);

        Assert.False(ReminderCalculator.SetNagPeriod(task, NagPeriod.OneHour, UtcAt(15, 15, 30)));

        Assert.Equal(UtcTime(15, 15), ReminderCalculator.DueInstant(task, UtcAt(15, 15, 30), Rules));
    }

    [Fact]
    public void A_Nag_on_a_Task_without_a_Due_is_refused()
    {
        var task = NaggingTask(null, null, null);

        Assert.Throws<ArgumentException>(() => ReminderCalculator.SetNagPeriod(task, NagPeriod.OneDay, UtcAt(15, 0)));
        Assert.Null(task.NagPeriod);
        Assert.False(ReminderCalculator.SetNagPeriod(task, null, UtcAt(15, 0)));
    }

    [Fact]
    public void Moving_the_Due_later_restarts_the_schedule_from_the_new_Due()
    {
        var task = NaggingTask(Monday, new TimeOnly(14, 0), NagPeriod.OneHour);
        task.NotifyAtDue = false;
        task.NotifiedThroughUtc = UtcTime(15, 15); // nagged at 17:00 local

        ReminderCalculator.ChangeDue(task, Monday, new TimeOnly(20, 0), UtcAt(15, 15, 30)); // 18:00 UTC

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(15, 18, 59), Rules));
        Assert.Equal(UtcTime(15, 19), ReminderCalculator.DueInstant(task, UtcAt(15, 19), Rules));
    }

    [Fact]
    public void A_Due_edit_of_a_nagging_Task_counts_past_moments_as_notified()
    {
        // "Notify at Due" off and only a Nag: a missed fixed Reminder is swallowed by the Due edit.
        var task = NaggingTask(Monday.AddDays(3), new TimeOnly(14, 0), NagPeriod.OneHour);
        task.Reminders.Add(new Reminder
        {
            Id = Guid.CreateVersion7(),
            TaskId = task.Id,
            Kind = ReminderKind.Fixed,
            AtUtc = UtcTime(15, 5),
        });
        task.NotifiedThroughUtc = UtcTime(15, 4);

        ReminderCalculator.ChangeDue(task, Monday.AddDays(4), new TimeOnly(14, 0), UtcAt(15, 6));

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(15, 6), Rules));
    }

    [Fact]
    public void Clearing_the_Due_turns_the_Nag_off()
    {
        var task = NaggingTask(Monday, new TimeOnly(14, 0), NagPeriod.ThreeHours);

        ReminderCalculator.ChangeDue(task, null, null, UtcAt(15, 0));

        Assert.Null(task.NagPeriod);
    }
}
