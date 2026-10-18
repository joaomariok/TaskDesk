using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Notifications;

/// <summary>
/// The fallback when rich notifications are unavailable (SPEC 7.3): a tray balloon with the same title
/// and body. It has no buttons; clicking it opens the Task of the last balloon, or the Today view after a
/// missed-Reminders summary. Manual checks only.
/// </summary>
internal sealed class TrayBalloonNotificationService(IBalloonTray tray) : INotificationService
{
    private NotificationActivation? _lastActivation;
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
        _lastActivation = new NotificationActivation(notification.TaskId, ReminderAction.Open);
        tray.ShowBalloon(notification.Title, notification.Body);
    }

    public void ShowSummary(MissedRemindersSummary summary)
    {
        _lastActivation = NotificationActivation.OpenToday;
        tray.ShowBalloon(summary.Title, summary.Body);
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
        if (_lastActivation is { } activation)
        {
            Activated?.Invoke(activation);
        }
    }
}
