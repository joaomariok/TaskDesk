using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using TaskDesk.Core.Reminders;

namespace TaskDesk.App.Notifications;

/// <summary>
/// Reminder toasts through the Windows App SDK (ADR 0002, SPEC 7.3), unpackaged. Covered by manual checks
/// only: tests never show a toast.
/// </summary>
internal sealed class AppSdkNotificationService(ILogger<AppSdkNotificationService> logger) : INotificationService
{
    private bool _registered;

    public event Action<NotificationActivation>? Activated;

    public void Register()
    {
        if (!AppNotificationManager.IsSupported())
        {
            throw new NotSupportedException("App notifications are not supported on this machine.");
        }

        var manager = AppNotificationManager.Default;
        manager.NotificationInvoked += OnNotificationInvoked; // subscribe before Register (ADR 0002)
        try
        {
            manager.Register();
        }
        catch
        {
            manager.NotificationInvoked -= OnNotificationInvoked;
            throw;
        }

        _registered = true;
        logger.LogInformation("App notifications registered; Windows setting: {Setting}", manager.Setting);
    }

    public NotificationActivation? GetLaunchActivation()
    {
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        return activation.Kind == ExtendedActivationKind.AppNotification
            && activation.Data is AppNotificationActivatedEventArgs args
                ? ReminderArguments.Parse(args.Arguments)
                : null;
    }

    public void Show(ReminderNotification notification)
    {
        var builder = new AppNotificationBuilder();
        foreach (var (key, value) in ReminderArguments.Open(notification.TaskId))
        {
            builder.AddArgument(key, value);
        }

        var markDone = new AppNotificationButton("Mark done");
        foreach (var (key, value) in ReminderArguments.MarkDone(notification.TaskId))
        {
            markDone.AddArgument(key, value);
        }

        var toast = builder
            .AddText(notification.Title)
            .AddText(notification.Body)
            .AddButton(markDone)
            .BuildNotification();
        AppNotificationManager.Default.Show(toast);
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        _registered = false;
        AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
        AppNotificationManager.Default.Unregister();
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (ReminderArguments.Parse(args.Arguments) is { } activation)
        {
            Activated?.Invoke(activation);
        }
    }
}
