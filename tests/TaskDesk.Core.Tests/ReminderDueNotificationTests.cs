using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public class ReminderDueNotificationTests
{
    // UTC+2 all year, with the default Reminder time at 09:00.
    private static readonly MomentRules Rules = new(
        TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2"), new TimeOnly(9, 0));

    private static readonly DateOnly Monday = new(2026, 6, 15);

    private static DateTimeOffset UtcAt(int day, int hour, int minute = 0) =>
        new(2026, 6, day, hour, minute, 0, TimeSpan.Zero);

    private static DateTime UtcTime(int day, int hour, int minute = 0) =>
        new(2026, 6, day, hour, minute, 0, DateTimeKind.Utc);

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

    [Fact]
    public void Notify_at_Due_is_on_for_a_new_Task()
    {
        Assert.True(new TaskItem().NotifyAtDue);
    }

    [Fact]
    public void A_timed_Due_notifies_once_at_its_Due_moment()
    {
        var task = TaskDue(Monday, new TimeOnly(14, 0)); // 12:00 UTC

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(15, 11, 59), Rules));
        Assert.Equal(UtcTime(15, 12), ReminderCalculator.DueInstant(task, UtcAt(15, 12), Rules));
        task.NotifiedThroughUtc = UtcTime(15, 12); // claimed
        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(20, 0), Rules));
    }

    [Fact]
    public void A_date_only_Due_notifies_at_the_default_Reminder_time_on_the_Due_date()
    {
        var task = TaskDue(Monday, null); // 09:00 local = 07:00 UTC

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(15, 6, 59), Rules));
        Assert.Equal(UtcTime(15, 7), ReminderCalculator.DueInstant(task, UtcAt(15, 7), Rules));
    }

    [Fact]
    public void With_Notify_at_Due_off_the_Due_never_notifies()
    {
        var task = TaskDue(Monday, new TimeOnly(14, 0));
        task.NotifyAtDue = false;

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(20, 0), Rules));
    }

    [Fact]
    public void Changing_the_Due_with_Notify_at_Due_on_counts_past_moments_as_notified()
    {
        // A missed fixed Reminder at 05:00 UTC; the Due edit at 06:00 swallows it, like any change to what makes
        // the Task notify.
        var task = TaskDue(Monday, null, ReminderValue.Fixed(UtcTime(15, 5)));
        task.NotifiedThroughUtc = UtcTime(15, 4);

        ReminderCalculator.ChangeDue(task, Monday.AddDays(1), null, UtcAt(15, 6));

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(15, 6), Rules));
    }

    [Fact]
    public void Moving_the_Due_later_re_arms_the_Due_notification()
    {
        var task = TaskDue(Monday, new TimeOnly(14, 0));
        task.NotifiedThroughUtc = UtcTime(15, 12); // notified at the Due

        ReminderCalculator.ChangeDue(task, Monday.AddDays(1), new TimeOnly(14, 0), UtcAt(15, 13));

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(16, 11, 59), Rules));
        Assert.Equal(UtcTime(16, 12), ReminderCalculator.DueInstant(task, UtcAt(16, 12), Rules));
    }

    [Fact]
    public void Moving_the_Due_into_the_past_never_notifies()
    {
        var task = TaskDue(Monday.AddDays(2), new TimeOnly(14, 0));
        task.NotifiedThroughUtc = UtcTime(15, 0);

        ReminderCalculator.ChangeDue(task, Monday, new TimeOnly(8, 0), UtcAt(15, 9));

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(15, 9), Rules));
    }

    [Fact]
    public void Clearing_the_Due_keeps_Notify_at_Due()
    {
        var task = TaskDue(Monday, null);
        task.NotifyAtDue = false;

        ReminderCalculator.ChangeDue(task, null, null, UtcAt(1, 0));
        Assert.False(task.NotifyAtDue);
        ReminderCalculator.ChangeDue(task, Monday, null, UtcAt(1, 0));

        Assert.False(task.NotifyAtDue);
    }

    [Fact]
    public void Switching_Notify_at_Due_counts_past_moments_as_notified()
    {
        var task = TaskDue(Monday, null, ReminderValue.Fixed(UtcTime(15, 5)));
        task.NotifyAtDue = false;
        task.NotifiedThroughUtc = UtcTime(15, 4);

        Assert.True(ReminderCalculator.SetNotifyAtDue(task, true, UtcAt(15, 8)));

        Assert.True(task.NotifyAtDue);
        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(15, 8), Rules)); // the Due at 07:00 is past too
        Assert.True(ReminderCalculator.SetNotifyAtDue(task, false, UtcAt(15, 9)));
        Assert.Equal(UtcTime(15, 9), task.NotifiedThroughUtc);
    }

    [Fact]
    public void Switching_Notify_at_Due_to_its_current_value_changes_nothing()
    {
        var task = TaskDue(Monday, null, ReminderValue.Fixed(UtcTime(15, 5)));
        task.NotifiedThroughUtc = UtcTime(15, 4);

        Assert.False(ReminderCalculator.SetNotifyAtDue(task, true, UtcAt(15, 8)));

        Assert.Equal(UtcTime(15, 4), task.NotifiedThroughUtc);
    }

    // ---- The upgrade rule ----

    [Fact]
    public void The_upgrade_counts_a_Due_already_past_as_notified()
    {
        var task = TaskDue(Monday, new TimeOnly(14, 0));

        ReminderCalculator.CountPastDueAsNotified(task, UtcAt(20, 0), Rules);

        Assert.Equal(UtcTime(15, 12), task.NotifiedThroughUtc);
        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(20, 0), Rules));
    }

    [Fact]
    public void The_upgrade_keeps_a_Due_still_ahead_armed()
    {
        var task = TaskDue(Monday.AddDays(10), new TimeOnly(14, 0));
        task.NotifiedThroughUtc = UtcTime(1, 0);

        ReminderCalculator.CountPastDueAsNotified(task, UtcAt(20, 0), Rules);

        Assert.Equal(UtcTime(1, 0), task.NotifiedThroughUtc);
        Assert.Equal(UtcTime(25, 12), ReminderCalculator.DueInstant(task, UtcAt(25, 12), Rules));
    }

    [Fact]
    public void The_upgrade_never_moves_notified_through_back()
    {
        var task = TaskDue(Monday, new TimeOnly(14, 0));
        task.NotifiedThroughUtc = UtcTime(18, 0);

        ReminderCalculator.CountPastDueAsNotified(task, UtcAt(20, 0), Rules);

        Assert.Equal(UtcTime(18, 0), task.NotifiedThroughUtc);
    }

    [Fact]
    public void The_upgrade_lets_an_armed_Reminder_already_past_fire_once()
    {
        // The Reminder at 05:00 UTC on the 15th was never shown; the Due at 12:00 is past too. One notification.
        var task = TaskDue(Monday, new TimeOnly(14, 0), ReminderValue.Fixed(UtcTime(15, 5)));
        task.NotifiedThroughUtc = UtcTime(15, 4);

        ReminderCalculator.CountPastDueAsNotified(task, UtcAt(20, 0), Rules);

        var due = ReminderCalculator.DueInstant(task, UtcAt(20, 0), Rules);
        Assert.NotNull(due);
        task.NotifiedThroughUtc = due; // claimed
        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(20, 0), Rules));
    }

    [Fact]
    public void The_upgrade_keeps_a_running_Snooze_running()
    {
        var task = TaskDue(Monday, new TimeOnly(14, 0));
        task.NotifiedThroughUtc = UtcTime(15, 12);
        task.SnoozedUntilUtc = UtcTime(20, 1);

        ReminderCalculator.CountPastDueAsNotified(task, UtcAt(20, 0), Rules);

        Assert.Null(ReminderCalculator.DueInstant(task, UtcAt(20, 0, 59), Rules));
        Assert.Equal(UtcTime(20, 1), ReminderCalculator.DueInstant(task, UtcAt(20, 1), Rules));
    }
}
