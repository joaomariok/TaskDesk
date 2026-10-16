using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation;

/// <summary>
/// Acts on a click on a Reminder notification, whether it arrived in the running process or launched it.
/// Call on the UI thread.
/// </summary>
public sealed class ReminderActivationHandler(MainViewModel viewModel, IMainWindow window)
{
    public async Task HandleAsync(NotificationActivation activation, CancellationToken cancellationToken = default)
    {
        switch (activation.Action)
        {
            case ReminderAction.Open:
                await viewModel.ShowTaskAsync(activation.TaskId, cancellationToken);
                window.ShowAndFocus();
                break;
            case ReminderAction.MarkDone:
                await viewModel.CompleteTaskAsync(activation.TaskId, cancellationToken);
                break;
        }
    }
}
