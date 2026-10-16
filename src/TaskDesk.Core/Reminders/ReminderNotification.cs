namespace TaskDesk.Core.Reminders;

/// <summary>A due Reminder with what its notification shows.</summary>
/// <param name="DueAtUtc">The instant the Reminder fired at: its Snooze when set, otherwise the planned Reminder.</param>
public sealed record DueReminder(
    Guid TaskId, string Title, DateOnly? DueDate, TimeOnly? DueTime, string ListName, DateTime DueAtUtc);

/// <summary>What a Reminder notification shows: title = Task title, body = Due and List name.</summary>
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
        return new ReminderNotification(reminder.TaskId, reminder.Title, body);
    }
}

/// <summary>What the owner did with a Reminder notification.</summary>
public enum ReminderAction
{
    /// <summary>Clicked the body: open the main window with the Task selected.</summary>
    Open,

    /// <summary>Clicked the Mark done button.</summary>
    MarkDone,
}

/// <summary>An activation of a Reminder notification.</summary>
public sealed record NotificationActivation(Guid TaskId, ReminderAction Action);

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

    public static IReadOnlyDictionary<string, string> Open(Guid taskId) => Build(OpenValue, taskId);

    public static IReadOnlyDictionary<string, string> MarkDone(Guid taskId) => Build(MarkDoneValue, taskId);

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

        if (!Guid.TryParse(taskId, out var id))
        {
            return null;
        }

        return action switch
        {
            OpenValue => new NotificationActivation(id, ReminderAction.Open),
            MarkDoneValue => new NotificationActivation(id, ReminderAction.MarkDone),
            _ => null,
        };
    }

    private static Dictionary<string, string> Build(string action, Guid taskId) => new()
    {
        [ActionKey] = action,
        [TaskIdKey] = taskId.ToString("D"),
    };
}
