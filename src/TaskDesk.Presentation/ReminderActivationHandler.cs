using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

/// <summary>
/// Acts on a click on a Reminder notification, whether it arrived in the running process or launched it.
/// Call on the UI thread.
/// </summary>
public sealed class ReminderActivationHandler(MainViewModel viewModel, IMainWindow window)
{
    /// <summary>Acts on <paramref name="activation"/>.</summary>
    /// <param name="activation">The notification click.</param>
    /// <param name="exitStarted">
    /// Cancelled when the app starts its exit (<see cref="ShellController.ExitStarted"/>): opening a Task or Today is
    /// then dropped and the window stays hidden, while Mark done and Snooze still complete, so the owner's action is
    /// kept.
    /// </param>
    public async Task HandleAsync(NotificationActivation activation, CancellationToken exitStarted = default)
    {
        switch (activation.Action)
        {
            case ReminderAction.Open:
                await viewModel.ShowTaskAsync(activation.TaskId, exitStarted);
                ShowWindowUnless(exitStarted);
                break;
            case ReminderAction.MarkDone:
                await viewModel.CompleteTaskAsync(activation.TaskId, CancellationToken.None);
                break;
            case ReminderAction.SnoozeShort:
                await viewModel.SnoozeTaskAsync(activation.TaskId, ReminderCalculator.ShortSnooze, CancellationToken.None);
                break;
            case ReminderAction.SnoozeLong:
                await viewModel.SnoozeTaskAsync(activation.TaskId, ReminderCalculator.LongSnooze, CancellationToken.None);
                break;
            case ReminderAction.OpenToday:
                await viewModel.ShowSmartViewAsync(SmartView.Today, exitStarted);
                ShowWindowUnless(exitStarted);
                break;
        }
    }

    // During the exit the window is about to close: showing it would bring it back for a moment.
    private void ShowWindowUnless(CancellationToken exitStarted)
    {
        if (!exitStarted.IsCancellationRequested)
        {
            window.ShowAndFocus();
        }
    }
}
