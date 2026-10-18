using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using TaskDesk.Core.Reminders;
using TaskDesk.Presentation;

namespace TaskDesk.App.Shell;

/// <summary>
/// Brings every Reminder activation to <see cref="ReminderActivationHandler"/> on the UI thread: a click on a
/// toast or tray balloon in the running app, the toast that launched it, and a later launch from a toast that the
/// single-instance pipe forwards (on a background thread). A failed action is logged, never thrown.
/// </summary>
internal sealed class ReminderActivationRouter(
    DispatcherQueue dispatcher, ReminderActivationHandler handler, ILogger<ReminderActivationRouter> logger)
{
    /// <summary>Hands <paramref name="notifications"/>' clicks to <see cref="Route"/>.</summary>
    public void Attach(INotificationService notifications) => notifications.Activated += Route;

    /// <summary>Queues the activation on the UI thread; callable from any thread.</summary>
    public void Route(NotificationActivation activation) => dispatcher.TryEnqueue(() => _ = HandleAsync(activation));

    /// <summary>Acts on the activation; call on the UI thread.</summary>
    public async Task HandleAsync(NotificationActivation activation, CancellationToken cancellationToken = default)
    {
        try
        {
            await handler.HandleAsync(activation, cancellationToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogError(ex, "Reminder notification action failed");
        }
    }
}
