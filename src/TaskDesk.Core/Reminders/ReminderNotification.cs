namespace TaskDesk.Core.Reminders;

/// <summary>A due Reminder with what its notification shows.</summary>
/// <param name="DueAtUtc">The instant the Reminder fired at: its Snooze when set, otherwise the planned Reminder.</param>
/// <param name="IsOverdue">
/// Whether the Task was already Overdue at the moment that fired, its due instant read in local time
/// (<see cref="TaskDesk.Core.Tasks.DueCalculator.IsOverdue"/>), not when it was listed.
/// </param>
public sealed record DueReminder(
    Guid TaskId, string Title, DateOnly? DueDate, TimeOnly? DueTime, string ListName, DateTime DueAtUtc,
    bool IsOverdue = false);

/// <summary>
/// What a Reminder notification shows: title = Task title, body = Due and List name, after "Overdue · " when the
/// Task is Overdue.
/// </summary>
public sealed record ReminderNotification(Guid TaskId, string Title, string Body)
{
    public static ReminderNotification For(DueReminder reminder, IFormatProvider culture)
    {
        var due = reminder.DueDate is { } date
            ? reminder.DueTime is { } time
                ? string.Format(culture, "Due {0:d} {1:t}", date, time)
                : string.Format(culture, "Due {0:d}", date)
            : null;
        var body = due is null ? reminder.ListName : $"{due} · {reminder.ListName}";
        if (reminder.IsOverdue)
        {
            body = $"Overdue · {body}"; // whatever made it fire
        }

        return new ReminderNotification(reminder.TaskId, reminder.Title, body);
    }
}

/// <summary>
/// One notification shown instead of a flood when more than
/// <see cref="ReminderCalculator.MissedSummaryThreshold"/> Reminders are due at once (SPEC 7.4); clicking it
/// opens the Today view.
/// </summary>
public sealed record MissedRemindersSummary(int Count)
{
    public string Title => $"{Count} reminders missed";

    public string Body => "Open Today to see them.";
}

/// <summary>What the owner did with a Reminder notification.</summary>
public enum ReminderAction
{
    /// <summary>Clicked the body: open the main window with the Task selected.</summary>
    Open,

    /// <summary>Clicked the Mark done button.</summary>
    MarkDone,

    /// <summary>Clicked the Snooze 10 min button (<see cref="ReminderCalculator.ShortSnooze"/>).</summary>
    SnoozeShort,

    /// <summary>Clicked the Snooze 1 h button (<see cref="ReminderCalculator.LongSnooze"/>).</summary>
    SnoozeLong,

    /// <summary>Clicked the missed-Reminders summary: open the main window on the Today view.</summary>
    OpenToday,
}

/// <summary>An activation of a Reminder notification.</summary>
/// <param name="TaskId">The Task of the Reminder; <see cref="Guid.Empty"/> for <see cref="ReminderAction.OpenToday"/>.</param>
public sealed record NotificationActivation(Guid TaskId, ReminderAction Action)
{
    /// <summary>The click on a missed-Reminders summary, which is about no single Task.</summary>
    public static NotificationActivation OpenToday { get; } = new(Guid.Empty, ReminderAction.OpenToday);
}

/// <summary>
/// The key/value arguments a Reminder notification carries, and their single parser, used both for a click
/// in the running process and for a launch by a click while TaskDesk was not running.
/// </summary>
public static class ReminderArguments
{
    public const string ActionKey = "action";
    public const string TaskIdKey = "taskId";
    private const string OpenValue = "open";
    private const string MarkDoneValue = "done";
    private const string SnoozeShortValue = "snooze-short";
    private const string SnoozeLongValue = "snooze-long";
    private const string OpenTodayValue = "today";

    public static IReadOnlyDictionary<string, string> Open(Guid taskId) => Build(OpenValue, taskId);

    public static IReadOnlyDictionary<string, string> MarkDone(Guid taskId) => Build(MarkDoneValue, taskId);

    public static IReadOnlyDictionary<string, string> SnoozeShort(Guid taskId) => Build(SnoozeShortValue, taskId);

    public static IReadOnlyDictionary<string, string> SnoozeLong(Guid taskId) => Build(SnoozeLongValue, taskId);

    /// <summary>The arguments of the missed-Reminders summary; they name no Task.</summary>
    public static IReadOnlyDictionary<string, string> OpenToday() =>
        new Dictionary<string, string> { [ActionKey] = OpenTodayValue };

    /// <summary>The arguments that <see cref="Parse"/> reads back as <paramref name="activation"/>.</summary>
    public static IReadOnlyDictionary<string, string> For(NotificationActivation activation) => activation.Action switch
    {
        ReminderAction.Open => Open(activation.TaskId),
        ReminderAction.MarkDone => MarkDone(activation.TaskId),
        ReminderAction.SnoozeShort => SnoozeShort(activation.TaskId),
        ReminderAction.SnoozeLong => SnoozeLong(activation.TaskId),
        ReminderAction.OpenToday => OpenToday(),
        _ => throw new ArgumentOutOfRangeException(nameof(activation), activation.Action, "Unknown Reminder action."),
    };

    /// <summary>The activation the arguments describe, or null when they are not a Reminder's.</summary>
    public static NotificationActivation? Parse(IEnumerable<KeyValuePair<string, string>> arguments)
    {
        string? action = null;
        string? taskId = null;
        foreach (var (key, value) in arguments)
        {
            if (key == ActionKey)
            {
                action = value;
            }
            else if (key == TaskIdKey)
            {
                taskId = value;
            }
        }

        if (action == OpenTodayValue)
        {
            return NotificationActivation.OpenToday;
        }

        if (!Guid.TryParse(taskId, out var id))
        {
            return null;
        }

        return action switch
        {
            OpenValue => new NotificationActivation(id, ReminderAction.Open),
            MarkDoneValue => new NotificationActivation(id, ReminderAction.MarkDone),
            SnoozeShortValue => new NotificationActivation(id, ReminderAction.SnoozeShort),
            SnoozeLongValue => new NotificationActivation(id, ReminderAction.SnoozeLong),
            _ => null,
        };
    }

    private static Dictionary<string, string> Build(string action, Guid taskId) => new()
    {
        [ActionKey] = action,
        [TaskIdKey] = taskId.ToString("D"),
    };
}
