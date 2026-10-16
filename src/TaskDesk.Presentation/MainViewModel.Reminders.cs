using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

// The Reminder of the selected Task, the Reminder notification actions, and the fallback banner.
public sealed partial class MainViewModel
{
    /// <summary>The local date of the selected Task's Reminder; null when it has none.</summary>
    [ObservableProperty]
    private DateTime? _detailReminderDate;

    /// <summary>
    /// The local time of the selected Task's Reminder as "H:mm" text. Left empty with a date, the Reminder
    /// fires at <see cref="ReminderCalculator.DefaultReminderTime"/>.
    /// </summary>
    [ObservableProperty]
    private string _detailReminderTime = "";

    /// <summary>Why rich notifications are unavailable, shown as a banner until dismissed; null hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotificationBanner))]
    private string? _notificationBanner;

    public bool HasNotificationBanner => NotificationBanner is not null;

    /// <summary>Shows the banner explaining that Reminders fall back to tray balloons.</summary>
    public void ShowNotificationBanner(string reason) => NotificationBanner = reason;

    [RelayCommand]
    private void DismissNotificationBanner() => NotificationBanner = null;

    // Called while _loadingDetail is set, so these assignments do not save.
    private void LoadReminderDetail(TaskItem? task)
    {
        if (task?.ReminderUtc is { } reminderUtc)
        {
            var (date, time) = ReminderCalculator.ToLocal(reminderUtc, _clock.LocalTimeZone);
            DetailReminderDate = date.ToDateTime(TimeOnly.MinValue);
            DetailReminderTime = FormatTime(time);
        }
        else
        {
            DetailReminderDate = null;
            DetailReminderTime = "";
        }
    }

    partial void OnDetailReminderDateChanged(DateTime? value)
    {
        if (_loadingDetail)
        {
            return;
        }

        if (value is null && DetailReminderTime.Length > 0)
        {
            // Clearing the date clears the whole Reminder.
            _loadingDetail = true;
            DetailReminderTime = "";
            _loadingDetail = false;
        }

        ScheduleReminderSave();
    }

    partial void OnDetailReminderTimeChanged(string value) => ScheduleReminderSave();

    private void ScheduleReminderSave()
    {
        if (_loadingDetail || SelectedTask is not { } row)
        {
            return;
        }

        TimeOnly? time = null;
        var text = DetailReminderTime?.Trim() ?? "";
        if (text.Length > 0)
        {
            if (!TimeOnly.TryParseExact(
                    text, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                ErrorMessage = "Enter the Reminder time as HH:mm, for example 14:30.";
                return;
            }

            time = parsed;
        }

        DateTime? reminderUtc = null;
        if (DetailReminderDate is { } picked)
        {
            reminderUtc = ReminderCalculator.ToUtc(DateOnly.FromDateTime(picked), time, _clock.LocalTimeZone);
            if (time is null)
            {
                // Show the time a date-only Reminder fires at.
                _loadingDetail = true;
                DetailReminderTime = FormatTime(ReminderCalculator.DefaultReminderTime);
                _loadingDetail = false;
            }
        }
        else if (time is not null)
        {
            ErrorMessage = "A Reminder time needs a Reminder date.";
            return;
        }

        ErrorMessage = null;
        _pendingSave = SaveReminderAsync(_pendingSave, row, reminderUtc);
    }

    private async Task SaveReminderAsync(Task previous, TaskRowViewModel row, DateTime? reminderUtc)
    {
        await previous; // saves of one Task never overlap
        try
        {
            row.Apply(await Reminders.SetReminderAsync(row.Id, reminderUtc));
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = "Could not save the changes: " + ex.Message;
        }
    }

    private static string FormatTime(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Selects a Task and opens its detail pane (a click on its Reminder notification). When the Task is not
    /// in the current view, the filters are cleared and the All view (or, for a completed Task, the Completed
    /// view) is shown instead.
    /// </summary>
    public async Task ShowTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var row = _all.Find(r => r.Id == taskId);
        if (row is null)
        {
            SearchText = "";
            MinPriorityFilter = null;
            HasDueFilter = false;
            HasReminderFilter = false;
            FilterTags.Clear();
            foreach (var view in new[] { SmartView.All, SmartView.Completed })
            {
                await ShowSmartViewAsync(view, cancellationToken);
                row = _all.Find(r => r.Id == taskId);
                if (row is not null)
                {
                    break;
                }
            }
        }

        if (row is null)
        {
            ErrorMessage = "That Task no longer exists.";
            return;
        }

        SelectedTask = row;
        IsDetailPaneOpen = true;
    }

    /// <summary>Completes a Task from its Reminder notification. A Task deleted since is ignored.</summary>
    public async Task CompleteTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            await taskService.SetCompletedAsync(taskId, true, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return;
        }

        await LoadAsync(cancellationToken); // also shows a next Occurrence the completion produced
        OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty);
    }
}
