using Microsoft.Extensions.Logging;
using TaskDesk.Core.Reminders;

namespace TaskDesk.Presentation;

/// <summary>
/// Delivers through rich notifications when they work, otherwise through the fallback (SPEC 7.3): if
/// registering or showing a rich notification fails, it logs the error, switches to the fallback for good,
/// and raises <see cref="FellBack"/> once so the main window can explain why.
/// </summary>
public sealed class NotificationDelivery : INotificationService
{
    private readonly INotificationService _primary;
    private readonly INotificationService _fallback;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private INotificationService _active;

    public NotificationDelivery(
        INotificationService primary, INotificationService fallback, ILogger<NotificationDelivery> logger)
    {
        _primary = primary;
        _fallback = fallback;
        _logger = logger;
        _active = primary;
        primary.Activated += OnActivated;
        fallback.Activated += OnActivated;
    }

    public event Action<NotificationActivation>? Activated;

    /// <summary>Raised once, with the reason, when rich notifications turn out to be unavailable.</summary>
    public event Action<string>? FellBack;

    /// <summary>Why rich notifications are unavailable, or null while they work.</summary>
    public string? FallbackReason { get; private set; }

    public void Register()
    {
        try
        {
            _primary.Register();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SwitchToFallback(ex, "register");
        }
    }

    public NotificationActivation? GetLaunchActivation() => Active.GetLaunchActivation();

    public void Show(ReminderNotification notification) => Deliver(service => service.Show(notification));

    public void ShowSummary(MissedRemindersSummary summary) => Deliver(service => service.ShowSummary(summary));

    private void Deliver(Action<INotificationService> show)
    {
        var active = Active;
        if (active == _primary)
        {
            try
            {
                show(_primary);
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                SwitchToFallback(ex, "show");
            }
        }

        show(_fallback);
    }

    public void Unregister() => Active.Unregister();

    private INotificationService Active
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    private void SwitchToFallback(Exception error, string step)
    {
        lock (_gate)
        {
            if (_active == _fallback)
            {
                return;
            }

            _active = _fallback;
        }

        _logger.LogWarning(error, "Rich notifications failed to {Step}; using the tray balloon fallback", step);
        _fallback.Register();
        FallbackReason = $"Windows notifications are unavailable ({error.Message}). Reminders show as tray balloons instead.";
        FellBack?.Invoke(FallbackReason);
    }

    private void OnActivated(NotificationActivation activation) => Activated?.Invoke(activation);
}
