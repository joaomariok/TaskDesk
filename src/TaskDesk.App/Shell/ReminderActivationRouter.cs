using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using TaskDesk.Core.Reminders;
using TaskDesk.Presentation;

namespace TaskDesk.App.Shell;

/// <summary>
/// Brings every Reminder activation to <see cref="ReminderActivationHandler"/> on the UI thread: a click on a
/// toast or tray balloon in the running app, and the toast that launched it. A failed action is logged, never
/// thrown.
/// </summary>
/// <param name="runActivation">
/// <see cref="ShellController.RunActivationAsync"/>: runs each activation that arrives in the running app with
/// <see cref="ShellController.ExitStarted"/> (it stops showing the window once the exit has started), and the exit
/// waits for it, so a Mark done or Snooze clicked as the exit starts is kept. Without it, activations run with no
/// exit to watch.
/// </param>
internal sealed class ReminderActivationRouter(
    DispatcherQueue dispatcher,
    ReminderActivationHandler handler,
    ILogger<ReminderActivationRouter> logger,
    Func<Func<CancellationToken, Task>, Task>? runActivation = null)
{
    /// <summary>Hands <paramref name="notifications"/>' clicks to <see cref="Route"/>.</summary>
    public void Attach(INotificationService notifications) => notifications.Activated += Route;

    /// <summary>
    /// Queues the activation on the UI thread; callable from any thread. The shell counts it from this call, so an
    /// exit that starts before the queued activation runs still waits for it.
    /// </summary>
    public void Route(NotificationActivation activation) =>
        _ = (runActivation ?? (work => work(CancellationToken.None)))(
            exitStarted => HandleOnUiThreadAsync(activation, exitStarted));

    // Completes once the activation has run, or at once when the UI thread is gone.
    private Task HandleOnUiThreadAsync(NotificationActivation activation, CancellationToken exitStarted)
    {
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() => _ = HandleThenSignalAsync(activation, exitStarted, handled)))
        {
            handled.SetResult();
        }

        return handled.Task;
    }

    // Signals in any case: the exit waits for the signal, so a failure that escapes HandleAsync must not hold it up.
    private async Task HandleThenSignalAsync(
        NotificationActivation activation, CancellationToken exitStarted, TaskCompletionSource handled)
    {
        try
        {
            await HandleAsync(activation, exitStarted);
        }
        finally
        {
            handled.TrySetResult();
        }
    }

    /// <summary>Acts on the activation; call on the UI thread.</summary>
    /// <param name="activation">The notification click.</param>
    /// <param name="exitStarted">Cancelled when the exit starts (see <see cref="ReminderActivationHandler.HandleAsync"/>).</param>
    public async Task HandleAsync(NotificationActivation activation, CancellationToken exitStarted = default)
    {
        try
        {
            await handler.HandleAsync(activation, exitStarted);
        }
        catch (OperationCanceledException) when (exitStarted.IsCancellationRequested)
        {
            logger.LogDebug("A Reminder notification's Open stopped for the exit");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogError(ex, "Reminder notification action failed");
        }
    }
}
