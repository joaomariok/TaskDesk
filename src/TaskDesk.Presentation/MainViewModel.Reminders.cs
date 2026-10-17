using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

// The Reminder and Snooze of the selected Task, the Reminder notification actions, and the fallback banner.
public sealed partial class MainViewModel
{
    /// <summary>The local date of the selected Task's Reminder; null when it has none.</summary>
    [ObservableProperty]
    private DateTime? _detailReminderDate;

    /// <summary>
    /// The local time of the selected Task's Reminder as "H:mm" text. Left empty with a date, the Reminder
    /// fires at the "Default reminder time" setting (<see cref="ReminderCalculator.DefaultReminderTime"/> when
    /// no settings are available).
    /// </summary>
    [ObservableProperty]
    private string _detailReminderTime = "";

    /// <summary>"Snoozed until ..." in local time for the selected Task; null when it is not snoozed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetailSnooze))]
    private string? _detailSnooze;

    public bool HasDetailSnooze => DetailSnooze is not null;

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

        LoadSnoozeDetail(task);
    }

    private void LoadSnoozeDetail(TaskItem? task)
    {
        if (task?.SnoozedUntilUtc is not { } snoozedUtc)
        {
            DetailSnooze = null;
            return;
        }

        var (date, time) = ReminderCalculator.ToLocal(snoozedUtc, _clock.LocalTimeZone);
        var today = DueCalculator.LocalToday(_clock.GetUtcNow(), _clock.LocalTimeZone);
        DetailSnooze = date == today
            ? $"Snoozed until {FormatTime(time)}"
            : $"Snoozed until {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {FormatTime(time)}";
    }

    /// <summary>Clears the Snooze of the selected Task, which re-arms its planned Reminder.</summary>
    [RelayCommand]
    private async Task ClearSnoozeAsync()
    {
        if (SelectedTask is not { } row)
        {
            return;
        }

        try
        {
            ApplyReminderChange(row, await Reminders.ClearSnoozeAsync(row.Id));
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = "Could not clear the Snooze: " + ex.Message;
        }
    }

    /// <summary>
    /// Snoozes a Task from its Reminder notification; the planned Reminder is unchanged. A Task deleted since
    /// is ignored.
    /// </summary>
    public async Task SnoozeTaskAsync(Guid taskId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        TaskItem task;
        try
        {
            task = await Reminders.SnoozeAsync(taskId, duration, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return;
        }

        if (_all.Find(r => r.Id == taskId) is { } row)
        {
            ApplyReminderChange(row, task);
        }
    }

    private void ApplyReminderChange(TaskRowViewModel row, TaskItem task)
    {
        row.Apply(task);
        if (SelectedTask == row)
        {
            LoadSnoozeDetail(task);
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
            // Read on every save, so a change in Settings applies without a restart.
            var defaultTime = DefaultReminderTime();
            reminderUtc = ReminderCalculator.ToUtc(
                DateOnly.FromDateTime(picked), time, _clock.LocalTimeZone, defaultTime);
            if (time is null)
            {
                // Show the time a date-only Reminder fires at.
                _loadingDetail = true;
                DetailReminderTime = FormatTime(defaultTime);
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
            ApplyReminderChange(row, await Reminders.SetReminderAsync(row.Id, reminderUtc));
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = "Could not save the changes: " + ex.Message;
        }
    }

    private TimeOnly DefaultReminderTime() =>
        settingsStore?.Load().DefaultReminderTime ?? ReminderCalculator.DefaultReminderTime;

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
