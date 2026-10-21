using System.Globalization;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public class ReminderBeforeDueTests
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

    private static TaskItem TaskDue(DateOnly? date, TimeOnly? time, params ReminderValue[] reminders)
    {
        var id = Guid.CreateVersion7();
        return new TaskItem
        {
            Id = id,
            Title = "Task",
            DueDate = date,
            DueTime = time,
            Reminders =
            [
                .. reminders.Select(v => new Reminder
                {
                    Id = Guid.CreateVersion7(), TaskId = id, Kind = v.Kind, AtUtc = v.AtUtc, Amount = v.Amount,
                    Unit = v.Unit,
                }),
            ],
        };
    }

    private static DateTime? MomentOf(TaskItem task) =>
        ReminderCalculator.MomentOf(Assert.Single(task.Reminders), task, Rules);

    public static TheoryData<string, string, string?, int, ReminderUnit, string> BeforeDueCases => new()
    {
        // description, Due date, Due time (null: date-only), amount, unit, expected UTC instant
        // Monday 2026-06-15, in summer time (UTC+2).
        { "1 day before a date-only Due: the day before at the default time", "2026-06-15", null, 1, ReminderUnit.Days, "2026-06-14T07:00" },
        { "2 hours before a date-only Due: counted from the default time", "2026-06-15", null, 2, ReminderUnit.Hours, "2026-06-15T05:00" },
        { "1 day before a timed Due keeps its time", "2026-06-15", "14:00", 1, ReminderUnit.Days, "2026-06-14T12:00" },
        { "1 week before", "2026-06-15", "14:00", 1, ReminderUnit.Weeks, "2026-06-08T12:00" },
        { "15 minutes before", "2026-06-15", "14:00", 15, ReminderUnit.Minutes, "2026-06-15T11:45" },
        { "minutes cross midnight", "2026-06-15", "00:10", 30, ReminderUnit.Minutes, "2026-06-14T21:40" },
        // Across the spring jump (Sunday 2026-03-29, 02:00 becomes 03:00).
        { "a day across the spring jump keeps the wall-clock time", "2026-03-29", "14:00", 1, ReminderUnit.Days, "2026-03-28T13:00" },
        { "a week across the spring jump keeps the wall-clock time", "2026-04-01", "09:00", 1, ReminderUnit.Weeks, "2026-03-25T08:00" },
        { "hours across the spring jump count elapsed time", "2026-03-29", "04:00", 3, ReminderUnit.Hours, "2026-03-28T23:00" },
        { "a day back into the skipped hour moves forward by the gap", "2026-03-30", "02:30", 1, ReminderUnit.Days, "2026-03-29T01:30" },
        // Across the autumn fall-back (Sunday 2026-10-25, 03:00 becomes 02:00 again).
        { "a day across the fall-back keeps the wall-clock time", "2026-10-25", "14:00", 1, ReminderUnit.Days, "2026-10-24T12:00" },
        { "a day back into the repeated hour takes its first occurrence", "2026-10-26", "02:30", 1, ReminderUnit.Days, "2026-10-25T00:30" },
        { "hours across the fall-back count elapsed time", "2026-10-25", "04:00", 3, ReminderUnit.Hours, "2026-10-25T00:00" },
    };

    [Theory]
    [MemberData(nameof(BeforeDueCases))]
    public void A_before_Due_Reminder_fires_its_amount_before_the_Due_moment(
        string description, string dueDate, string? dueTime, int amount, ReminderUnit unit, string expected)
    {
        var task = TaskDue(
            DateOnly.Parse(dueDate, CultureInfo.InvariantCulture),
            dueTime is null ? null : TimeOnly.Parse(dueTime, CultureInfo.InvariantCulture),
            ReminderValue.BeforeDue(amount, unit));

        var moment = MomentOf(task);

        Assert.True(
            DateTime.SpecifyKind(DateTime.Parse(expected, CultureInfo.InvariantCulture), DateTimeKind.Utc) == moment,
            $"{description}: {moment:O}");
        Assert.Equal(DateTimeKind.Utc, moment!.Value.Kind);
    }

    private static readonly DateOnly Monday = new(2026, 6, 15);

    private static DateTimeOffset UtcAt(int day, int hour, int minute = 0, int second = 0) =>
        new(2026, 6, day, hour, minute, second, TimeSpan.Zero);

    private static DateTime UtcTime(int day, int hour, int minute = 0) =>
        new(2026, 6, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void The_default_Reminder_time_comes_from_the_rules_given()
    {
        var task = TaskDue(Monday, null, ReminderValue.BeforeDue(2, ReminderUnit.Hours));

        var moment = ReminderCalculator.MomentOf(
            task.Reminders[0], task, Rules with { DefaultTime = new TimeOnly(7, 0) });

        Assert.Equal(UtcTime(15, 3), moment); // 07:00 local (UTC+2) on Monday, less 2 hours
    }

    [Fact]
    public void A_before_Due_Reminder_of_a_Task_without_a_Due_has_no_moment()
    {
        var task = TaskDue(null, null, ReminderValue.BeforeDue(1, ReminderUnit.Days));

        Assert.Null(MomentOf(task));
    }

    [Fact]
    public void A_Task_is_due_at_its_before_Due_moment()
    {
        // Due Monday 14:00 local (12:00 UTC); 1 hour before is 11:00 UTC.
        var task = TaskDue(Monday, new TimeOnly(14, 0), ReminderValue.BeforeDue(1, ReminderUnit.Hours));
        task.NotifiedThroughUtc = UtcTime(15, 9);

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(15, 10, 59), Rules));
        Assert.Equal(UtcTime(15, 11), ReminderCalculator.DueInstant(task, UtcAt(15, 11), Rules));
        Assert.Equal([task.Id], ReminderCalculator.SelectDue([task], UtcAt(15, 11), Rules).Select(t => t.Id));
    }

    [Fact]
    public void Before_Due_and_fixed_Reminders_are_kept_in_the_order_they_fire()
    {
        var task = TaskDue(Monday, new TimeOnly(14, 0));

        ReminderCalculator.AddReminder(task, ReminderValue.BeforeDue(1, ReminderUnit.Hours), UtcAt(1, 0), Rules);
        ReminderCalculator.AddReminder(task, ReminderValue.Fixed(UtcTime(15, 11, 30)), UtcAt(1, 0), Rules);
        ReminderCalculator.AddReminder(task, ReminderValue.BeforeDue(1, ReminderUnit.Days), UtcAt(1, 0), Rules);

        Assert.Equal(
            [
                ReminderValue.BeforeDue(1, ReminderUnit.Days), ReminderValue.BeforeDue(1, ReminderUnit.Hours),
                ReminderValue.Fixed(UtcTime(15, 11, 30)),
            ],
            task.Reminders.Select(r => r.Value));
    }

    [Fact]
    public void The_same_amount_and_unit_twice_is_refused()
    {
        var task = TaskDue(Monday, null, ReminderValue.BeforeDue(1, ReminderUnit.Days));

        var ex = Assert.Throws<ArgumentException>(() => ReminderCalculator.AddReminder(
            task, ReminderValue.BeforeDue(1, ReminderUnit.Days), UtcAt(1, 0), Rules));
        ReminderCalculator.AddReminder(task, ReminderValue.BeforeDue(24, ReminderUnit.Hours), UtcAt(1, 0), Rules);

        Assert.Equal("The Task already has this Reminder.", ex.Message);
        Assert.Equal(2, task.Reminders.Count);
    }

    [Fact]
    public void A_before_Due_Reminder_on_a_Task_without_a_Due_is_refused()
    {
        var task = TaskDue(null, null);

        var ex = Assert.Throws<ArgumentException>(() => ReminderCalculator.AddReminder(
            task, ReminderValue.BeforeDue(1, ReminderUnit.Days), UtcAt(1, 0), Rules));

        Assert.Equal("A Reminder before Due needs a Due.", ex.Message);
        Assert.Empty(task.Reminders);
    }

    public static TheoryData<string, int, ReminderUnit?, DateTime?> InvalidBeforeDueCases => new()
    {
        { "amount 0", 0, ReminderUnit.Days, null },
        { "amount 1000", 1000, ReminderUnit.Minutes, null },
        { "a negative amount", -1, ReminderUnit.Hours, null },
        { "no unit", 5, null, null },
        { "an unknown unit", 5, (ReminderUnit)9, null },
        { "an instant as well", 5, ReminderUnit.Days, new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc) },
    };

    [Theory]
    [MemberData(nameof(InvalidBeforeDueCases))]
    public void A_before_Due_Reminder_needs_an_amount_from_1_to_999_and_a_unit(
        string description, int amount, ReminderUnit? unit, DateTime? atUtc)
    {
        var task = TaskDue(Monday, null);

        Assert.Throws<ArgumentException>(() => ReminderCalculator.AddReminder(
            task, new ReminderValue(ReminderKind.BeforeDue, atUtc, amount, unit), UtcAt(1, 0), Rules));
        Assert.True(task.Reminders.Count == 0, description);
    }

    [Fact]
    public void A_before_Due_Reminder_holds_its_amount_and_unit_as_given()
    {
        var task = TaskDue(Monday, null);

        var added = ReminderCalculator.AddReminder(
            task, ReminderValue.BeforeDue(999, ReminderUnit.Weeks), UtcAt(1, 0), Rules);
        Assert.Equal((999, ReminderUnit.Weeks), (added.Amount!.Value, added.Unit!.Value));
        var changed = ReminderCalculator.UpdateReminder(
            task, added.Id, ReminderValue.BeforeDue(1, ReminderUnit.Minutes), UtcAt(1, 0), Rules);

        Assert.True(changed);
        Assert.Equal(ReminderKind.BeforeDue, added.Kind);
        Assert.Equal((1, ReminderUnit.Minutes), (added.Amount!.Value, added.Unit!.Value));
        Assert.Null(added.AtUtc);
    }

    [Fact]
    public void A_fixed_Reminder_changed_to_before_Due_drops_its_instant()
    {
        var task = TaskDue(Monday, null, ReminderValue.Fixed(UtcTime(14, 8)));

        ReminderCalculator.UpdateReminder(
            task, task.Reminders[0].Id, ReminderValue.BeforeDue(2, ReminderUnit.Days), UtcAt(1, 0), Rules);

        Assert.Equal(ReminderValue.BeforeDue(2, ReminderUnit.Days), task.Reminders[0].Value);
    }

    [Fact]
    public void Changing_the_Due_counts_moments_already_past_as_notified()
    {
        // Due Monday 14:00 local, moved at 06:00 UTC to Monday 10:00: "1 day before" moves into the past and never
        // fires; "1 hour before" moves to 07:00 UTC, still ahead.
        var task = TaskDue(Monday, new TimeOnly(14, 0),
            ReminderValue.BeforeDue(1, ReminderUnit.Days), ReminderValue.BeforeDue(1, ReminderUnit.Hours));
        task.NotifiedThroughUtc = UtcTime(13, 0);

        var removed = ReminderCalculator.ChangeDue(task, Monday, new TimeOnly(10, 0), UtcAt(15, 6));

        Assert.Empty(removed);
        Assert.Equal((Monday, new TimeOnly(10, 0)), (task.DueDate!.Value, task.DueTime!.Value));
        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(15, 6, 59, 59), Rules));
        Assert.Equal(UtcTime(15, 7), ReminderCalculator.DueInstant(task, UtcAt(15, 7), Rules));
    }

    [Fact]
    public void Changing_the_Due_of_a_Task_with_only_fixed_Reminders_keeps_a_missed_one()
    {
        // No moment of this Task depends on the Due ("Notify at Due" is off), so the change is like an edit to any
        // other field.
        var task = TaskDue(Monday, null, ReminderValue.Fixed(UtcTime(15, 5)));
        task.NotifyAtDue = false;
        task.NotifiedThroughUtc = UtcTime(15, 4);

        ReminderCalculator.ChangeDue(task, Monday.AddDays(1), null, UtcAt(15, 6));

        Assert.Equal(UtcTime(15, 5), ReminderCalculator.DueInstant(task, UtcAt(15, 6), Rules));
    }

    [Fact]
    public void Moving_the_Due_later_re_arms_a_before_Due_Reminder_that_had_fired()
    {
        var task = TaskDue(Monday, new TimeOnly(14, 0), ReminderValue.BeforeDue(1, ReminderUnit.Hours));
        task.NotifiedThroughUtc = UtcTime(15, 11); // fired at 11:00 UTC

        ReminderCalculator.ChangeDue(task, Monday.AddDays(1), new TimeOnly(14, 0), UtcAt(15, 12));

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(16, 10, 59), Rules));
        Assert.Equal(UtcTime(16, 11), ReminderCalculator.DueInstant(task, UtcAt(16, 11), Rules));
    }

    [Fact]
    public void Clearing_the_Due_removes_the_before_Due_Reminders_and_keeps_the_fixed_ones()
    {
        var task = TaskDue(Monday, null,
            ReminderValue.BeforeDue(1, ReminderUnit.Days), ReminderValue.Fixed(UtcTime(20, 8)),
            ReminderValue.BeforeDue(5, ReminderUnit.Minutes));

        var removed = ReminderCalculator.ChangeDue(task, null, null, UtcAt(1, 0));

        Assert.Equal(
            [ReminderValue.BeforeDue(1, ReminderUnit.Days), ReminderValue.BeforeDue(5, ReminderUnit.Minutes)],
            removed.Select(r => r.Value));
        Assert.Equal([ReminderValue.Fixed(UtcTime(20, 8))], task.Reminders.Select(r => r.Value));
        Assert.Null(task.DueDate);
    }
}
