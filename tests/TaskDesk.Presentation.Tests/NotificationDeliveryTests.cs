using Microsoft.Extensions.Logging;
using TaskDesk.Core.Reminders;

namespace TaskDesk.Presentation.Tests;

public class NotificationDeliveryTests
{
    private static readonly ReminderNotification Toast = new(Guid.NewGuid(), "Pay rent", "Inbox");

    private sealed class ListLogger : ILogger<NotificationDelivery>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static (FakeNotificationService Primary, FakeNotificationService Fallback, ListLogger Log, NotificationDelivery Delivery)
        Create()
    {
        var primary = new FakeNotificationService();
        var fallback = new FakeNotificationService();
        var log = new ListLogger();
        return (primary, fallback, log, new NotificationDelivery(primary, fallback, log));
    }

    [Fact]
    public void When_rich_notifications_register_they_deliver_and_the_fallback_stays_unused()
    {
        var (primary, fallback, _, delivery) = Create();
        string? reason = null;
        delivery.FellBack += r => reason = r;

        delivery.Register();
        delivery.Show(Toast);

        Assert.Equal([Toast], primary.Shown);
        Assert.Equal(0, fallback.RegisterCalls);
        Assert.Null(reason);
        Assert.Null(delivery.FallbackReason);
    }

    [Fact]
    public void When_registration_fails_it_logs_switches_to_the_fallback_and_reports_why()
    {
        var (primary, fallback, log, delivery) = Create();
        primary.RegisterFailure = new InvalidOperationException("Windows App Runtime is not installed");
        string? reason = null;
        delivery.FellBack += r => reason = r;

        delivery.Register();
        delivery.Show(Toast);

        Assert.Empty(primary.Shown);
        Assert.Equal([Toast], fallback.Shown);
        Assert.Equal(1, fallback.RegisterCalls);
        Assert.Contains("Windows App Runtime is not installed", reason);
        Assert.Equal(reason, delivery.FallbackReason);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void When_showing_a_rich_notification_fails_the_fallback_shows_it_and_takes_over()
    {
        var (primary, fallback, log, delivery) = Create();
        delivery.Register();
        var reasons = new List<string>();
        delivery.FellBack += reasons.Add;
        primary.ShowFailure = new InvalidOperationException("toast failed");

        delivery.Show(Toast);
        delivery.Show(Toast);

        Assert.Equal([Toast, Toast], fallback.Shown);
        Assert.Single(reasons);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void A_missed_Reminders_summary_is_delivered_like_a_Reminder_and_falls_back_on_failure()
    {
        var (primary, fallback, _, delivery) = Create();
        delivery.Register();
        var summary = new MissedRemindersSummary(5);

        delivery.ShowSummary(summary);
        primary.ShowFailure = new InvalidOperationException("toast failed");
        delivery.ShowSummary(summary);

        Assert.Equal([summary], primary.Summaries);
        Assert.Equal([summary], fallback.Summaries);
        Assert.NotNull(delivery.FallbackReason);
    }

    [Fact]
    public void Clicks_on_either_delivery_are_reported()
    {
        var (primary, fallback, _, delivery) = Create();
        var clicks = new List<NotificationActivation>();
        delivery.Activated += clicks.Add;
        delivery.Register();
        var open = new NotificationActivation(Guid.NewGuid(), ReminderAction.Open);
        var done = new NotificationActivation(Guid.NewGuid(), ReminderAction.MarkDone);

        primary.Click(open);
        fallback.Click(done);

        Assert.Equal([open, done], clicks);
    }

    [Fact]
    public void The_launch_activation_and_unregistration_go_to_the_delivery_in_use()
    {
        var (primary, fallback, _, delivery) = Create();
        var launch = new NotificationActivation(Guid.NewGuid(), ReminderAction.Open);
        primary.LaunchActivation = launch;

        delivery.Register();
        var got = delivery.GetLaunchActivation();
        delivery.Unregister();

        Assert.Equal(launch, got);
        Assert.Equal(1, primary.UnregisterCalls);
        Assert.Equal(0, fallback.UnregisterCalls);
    }
}
