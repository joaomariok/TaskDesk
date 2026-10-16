using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Reminders;

namespace TaskDesk.Presentation.Tests;

public class ReminderSchedulerTests
{
    private static readonly DateTime Start = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Setup(
        FakeTimeProvider Clock,
        FakeReminderService Reminders,
        FakeNotificationService Notifications,
        FakeTaskChanges Changes,
        ReminderScheduler Scheduler);

    private static Setup Create()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(Start));
        var reminders = new FakeReminderService(clock);
        var notifications = new FakeNotificationService();
        var changes = new FakeTaskChanges();
        return new Setup(clock, reminders, notifications, changes,
            new ReminderScheduler(reminders, notifications, changes, clock));
    }

    [Fact]
    public async Task Starting_checks_at_once_and_shows_each_due_Reminder()
    {
        var s = Create();
        var task = s.Reminders.Add("Pay rent", Start.AddMinutes(-5));

        await s.Scheduler.StartAsync();

        var shown = Assert.Single(s.Notifications.Shown);
        Assert.Equal(task.Id, shown.TaskId);
        Assert.Equal("Pay rent", shown.Title);
        Assert.Equal("Inbox", shown.Body);
        Assert.Equal(Start, task.ReminderFiredUtc);
    }

    [Fact]
    public async Task A_Reminder_that_becomes_due_is_shown_by_the_next_30_second_check()
    {
        var s = Create();
        s.Reminders.Add("a", Start.AddSeconds(20));
        await s.Scheduler.StartAsync();
        Assert.Empty(s.Notifications.Shown);

        s.Clock.Advance(TimeSpan.FromSeconds(29));
        await s.Scheduler.CurrentCheck;
        Assert.Empty(s.Notifications.Shown);

        s.Clock.Advance(TimeSpan.FromSeconds(1));
        await s.Scheduler.CurrentCheck;
        Assert.Single(s.Notifications.Shown);
        Assert.Equal(TimeSpan.FromSeconds(30), ReminderScheduler.Interval);
    }

    [Fact]
    public async Task Each_Reminder_fires_once_across_checks()
    {
        var s = Create();
        s.Reminders.Add("a", Start.AddMinutes(-1));
        await s.Scheduler.StartAsync();

        for (var i = 0; i < 3; i++)
        {
            s.Clock.Advance(ReminderScheduler.Interval);
            await s.Scheduler.CurrentCheck;
        }

        Assert.Single(s.Notifications.Shown);
        Assert.Equal(4, s.Reminders.ListCalls);
    }

    [Fact]
    public async Task A_Task_change_triggers_a_check_without_waiting_for_the_timer()
    {
        var s = Create();
        await s.Scheduler.StartAsync();
        var task = s.Reminders.Add("a", null);

        await s.Reminders.SetReminderAsync(task.Id, Start, TestContext.Current.CancellationToken);
        s.Changes.Raise();
        await s.Scheduler.CurrentCheck;

        Assert.Single(s.Notifications.Shown);
    }

    [Fact]
    public async Task A_requested_check_shows_what_became_due_while_asleep()
    {
        var s = Create();
        s.Reminders.Add("a", Start.AddHours(2));
        await s.Scheduler.StartAsync();

        s.Clock.SetUtcNow(new DateTimeOffset(Start.AddHours(3))); // resume: timers may not have run
        await s.Scheduler.RequestCheck();

        Assert.Single(s.Notifications.Shown);
    }

    [Fact]
    public async Task A_Reminder_claimed_elsewhere_is_not_shown()
    {
        var s = Create();
        var task = s.Reminders.Add("a", Start.AddMinutes(-1));
        task.IsCompleted = true; // completed between listing and claiming: TryMarkFired refuses

        await s.Scheduler.StartAsync();

        Assert.Empty(s.Notifications.Shown);
    }

    [Fact]
    public async Task A_failed_check_does_not_stop_later_checks()
    {
        var s = Create();
        s.Reminders.Add("a", Start.AddMinutes(-1));
        s.Reminders.FailNextList = new InvalidOperationException("database locked");

        await s.Scheduler.StartAsync();
        Assert.Empty(s.Notifications.Shown);

        s.Clock.Advance(ReminderScheduler.Interval);
        await s.Scheduler.CurrentCheck;
        Assert.Single(s.Notifications.Shown);
    }

    [Fact]
    public async Task After_dispose_no_more_checks_run()
    {
        var s = Create();
        await s.Scheduler.StartAsync();

        s.Scheduler.Dispose();
        s.Reminders.Add("a", Start);
        s.Clock.Advance(ReminderScheduler.Interval);
        await s.Scheduler.CurrentCheck;

        Assert.Empty(s.Notifications.Shown);
        Assert.False(s.Changes.HasListeners);
    }

    [Fact]
    public async Task Due_Reminders_are_shown_first_due_first()
    {
        var s = Create();
        s.Reminders.Add("later", Start.AddMinutes(-1));
        s.Reminders.Add("earlier", Start.AddMinutes(-9));

        await s.Scheduler.StartAsync();

        Assert.Equal(["earlier", "later"], s.Notifications.Shown.Select(n => n.Title));
    }
}
