using TaskDesk.App.Shell;
using TaskDesk.Core.Reminders;

namespace TaskDesk.App.Notifications;

/// <summary>
/// The fallback when rich notifications are unavailable (SPEC 7.3): a tray balloon with the same title
/// and body. It has no buttons; clicking it opens the Task of the last balloon. Manual checks only.
/// </summary>
internal sealed class TrayBalloonNotificationService(HNotifyTrayIcon tray) : INotificationService
{
    private Guid? _lastTaskId;
    private bool _registered;

    public event Action<NotificationActivation>? Activated;

    public void Register()
    {
        if (!_registered)
        {
            _registered = true;
            tray.BalloonClicked += OnBalloonClicked;
        }
    }

    public NotificationActivation? GetLaunchActivation() => null;

    public void Show(ReminderNotification notification)
    {
        _lastTaskId = notification.TaskId;
        tray.ShowBalloon(notification.Title, notification.Body);
    }

    public void Unregister()
    {
        if (_registered)
        {
            _registered = false;
            tray.BalloonClicked -= OnBalloonClicked;
        }
    }

    private void OnBalloonClicked()
    {
        if (_lastTaskId is { } taskId)
        {
            Activated?.Invoke(new NotificationActivation(taskId, ReminderAction.Open));
        }
    }
}
