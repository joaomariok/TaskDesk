namespace TaskDesk.Core.Reminders;

/// <summary>Delivers Reminder notifications to Windows and reports what the owner did with them.</summary>
public interface INotificationService
{
    /// <summary>Raised when the owner clicks a notification or its Mark done button; may be raised on any thread.</summary>
    event Action<NotificationActivation>? Activated;

    /// <summary>Prepares delivery; call once at start-up before the first notification.</summary>
    /// <exception cref="Exception">This delivery is unavailable on this machine.</exception>
    void Register();

    /// <summary>The click on a notification that launched this process, or null.</summary>
    NotificationActivation? GetLaunchActivation();

    /// <summary>Shows a Reminder notification.</summary>
    void Show(ReminderNotification notification);

    /// <summary>Shows the one notification that stands for many missed Reminders.</summary>
    void ShowSummary(MissedRemindersSummary summary);

    /// <summary>Stops delivery; call before the app exits.</summary>
    void Unregister();
}
