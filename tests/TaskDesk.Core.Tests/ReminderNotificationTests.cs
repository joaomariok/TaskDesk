using System.Globalization;
using TaskDesk.Core.Reminders;

namespace TaskDesk.Core.Tests;

public class ReminderNotificationTests
{
    private static readonly Guid TaskId = Guid.Parse("0199f0c4-0000-7000-8000-000000000001");
    private static readonly DateTime DueAt = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static ReminderNotification For(DateOnly? dueDate, TimeOnly? dueTime) =>
        ReminderNotification.For(
            new DueReminder(TaskId, "Pay rent", dueDate, dueTime, "Home", DueAt), CultureInfo.InvariantCulture);

    [Fact]
    public void The_title_is_the_Task_title_and_the_body_the_Due_and_List_name()
    {
        var notification = For(new DateOnly(2026, 10, 20), new TimeOnly(14, 30));

        Assert.Equal(TaskId, notification.TaskId);
        Assert.Equal("Pay rent", notification.Title);
        Assert.Equal("Due 10/20/2026 14:30 · Home", notification.Body);
    }

    [Fact]
    public void A_date_only_Due_shows_the_date_alone()
    {
        Assert.Equal("Due 10/20/2026 · Home", For(new DateOnly(2026, 10, 20), null).Body);
    }

    [Fact]
    public void Without_a_Due_the_body_is_the_List_name()
    {
        Assert.Equal("Home", For(null, null).Body);
    }

    [Fact]
    public void Open_and_Mark_done_arguments_round_trip_through_the_parser()
    {
        Assert.Equal(
            new NotificationActivation(TaskId, ReminderAction.Open),
            ReminderArguments.Parse(ReminderArguments.Open(TaskId)));
        Assert.Equal(
            new NotificationActivation(TaskId, ReminderAction.MarkDone),
            ReminderArguments.Parse(ReminderArguments.MarkDone(TaskId)));
    }

    [Fact]
    public void Snooze_arguments_round_trip_through_the_parser()
    {
        Assert.Equal(
            new NotificationActivation(TaskId, ReminderAction.SnoozeShort),
            ReminderArguments.Parse(ReminderArguments.SnoozeShort(TaskId)));
        Assert.Equal(
            new NotificationActivation(TaskId, ReminderAction.SnoozeLong),
            ReminderArguments.Parse(ReminderArguments.SnoozeLong(TaskId)));
    }

    [Fact]
    public void Open_Today_arguments_name_no_Task_and_round_trip_through_the_parser()
    {
        Assert.False(ReminderArguments.OpenToday().ContainsKey(ReminderArguments.TaskIdKey));
        Assert.Equal(NotificationActivation.OpenToday, ReminderArguments.Parse(ReminderArguments.OpenToday()));
        Assert.Equal(ReminderAction.OpenToday, NotificationActivation.OpenToday.Action);
    }

    [Theory]
    [InlineData(4, "4 reminders missed")]
    [InlineData(12, "12 reminders missed")]
    public void The_missed_Reminders_summary_says_how_many_were_missed(int count, string title)
    {
        var summary = new MissedRemindersSummary(count);

        Assert.Equal(title, summary.Title);
        Assert.Equal("Open Today to see them.", summary.Body);
    }

    public static TheoryData<string, string?, string?> InvalidArguments => new()
    {
        { "no arguments", null, null },
        { "no task id", "open", null },
        { "a task id that is not a Guid", "open", "abc" },
        { "an unknown action", "explode", "0199f0c4-0000-7000-8000-000000000001" },
        { "no action", null, "0199f0c4-0000-7000-8000-000000000001" },
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void Arguments_that_do_not_name_an_action_and_a_Task_parse_to_nothing(
        string description, string? action, string? taskId)
    {
        var arguments = new Dictionary<string, string>();
        if (action is not null)
        {
            arguments[ReminderArguments.ActionKey] = action;
        }

        if (taskId is not null)
        {
            arguments[ReminderArguments.TaskIdKey] = taskId;
        }

        Assert.True(ReminderArguments.Parse(arguments) is null, description);
    }
}
