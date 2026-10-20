using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

// The Reminders and Snooze of the selected Task, the Reminder notification actions, and the fallback banner.
public sealed partial class MainViewModel
{
    private ReminderEditorViewModel? _reminderEditor;

    /// <summary>The selected Task's Reminders, the one that fires first first.</summary>
    public System.Collections.ObjectModel.ObservableCollection<ReminderRowViewModel> DetailReminders { get; } = [];

    /// <summary>The Reminder flyout, opened by "Add reminder" or a click on a row.</summary>
    public ReminderEditorViewModel ReminderEditor => _reminderEditor ??= new(DefaultReminderTime, SaveReminder);

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

    private void LoadReminderDetail(TaskItem? task)
    {
        ReminderEditor.CancelCommand.Execute(null);
        LoadReminderRows(task);
        LoadSnoozeDetail(task);
    }

    private void LoadReminderRows(TaskItem? task)
    {
        DetailReminders.Clear();
        if (task is null)
        {
            return;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var today = DueCalculator.LocalToday(_clock.GetUtcNow(), _clock.LocalTimeZone);
        var rows = task.Reminders
            .Select(r => (Reminder: r, At: ReminderCalculator.MomentOf(r)))
            .Where(r => r.At is not null)
            .OrderBy(r => r.At)
            .ThenBy(r => r.Reminder.Id);
        foreach (var (reminder, at) in rows)
        {
            var (date, time) = ReminderCalculator.ToLocal(at!.Value, _clock.LocalTimeZone);
            DetailReminders.Add(new ReminderRowViewModel(
                reminder.Id, at.Value, DueDisplay.Format(date, time, today), at.Value <= now, EditReminder,
                RemoveReminderAsync));
        }
    }

    /// <summary>Opens the Reminder flyout on a new Reminder ("Add reminder").</summary>
    [RelayCommand]
    private void AddReminder() => ReminderEditor.Open(null, null, null);

    private void EditReminder(ReminderRowViewModel row)
    {
        var (date, time) = ReminderCalculator.ToLocal(row.FiresAtUtc, _clock.LocalTimeZone);
        ReminderEditor.Open(row.Id, date.ToDateTime(TimeOnly.MinValue), time.ToTimeSpan());
    }

    // The flyout's Save: false keeps it open with the error under Reminders.
    private bool SaveReminder(ReminderEditorViewModel editor)
    {
        if (DetailTask is not { } row)
        {
            return false;
        }

        if (editor.Date is not { } day)
        {
            ShowFieldError(
                ErrorPlace.Reminder,
                editor.Time is null ? "A Reminder needs a date." : "A Reminder time needs a Reminder date.");
            return false;
        }

        // The default time is read on every save, so a change in Settings applies without a restart.
        TimeOnly? time = editor.Time is { } picked ? TimeOnly.FromTimeSpan(picked) : null;
        var value = ReminderValue.Fixed(ReminderCalculator.ToUtc(
            DateOnly.FromDateTime(day), time, _clock.LocalTimeZone, DefaultReminderTime()));
        var editingId = editor.EditingId;
        _ = Saves.Enqueue(
            () => StoreReminderAsync(row, () => editingId is { } id
                ? Reminders.UpdateReminderAsync(id, value)
                : Reminders.AddReminderAsync(row.Id, value)),
            accepted: () => ErrorMessage = null);
        return true;
    }

    private Task RemoveReminderAsync(ReminderRowViewModel reminder)
    {
        if (DetailTask is not { } row)
        {
            return Task.CompletedTask;
        }

        return Saves.Enqueue(
            () => StoreReminderAsync(row, () => Reminders.RemoveReminderAsync(reminder.Id)),
            accepted: () => ErrorMessage = null) ?? Task.CompletedTask;
    }

    // A refused Reminder (a duplicate) shows its reason under Reminders and stores nothing.
    private async Task<bool> StoreReminderAsync(TaskRowViewModel row, Func<Task<TaskItem>> store)
    {
        TaskItem task;
        try
        {
            task = await store();
        }
        catch (ArgumentException ex)
        {
            ShowFieldError(ErrorPlace.Reminder, ex.Message);
            return false;
        }

        ApplyReminderChange(row, task);
        return true;
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
    private Task ClearSnoozeAsync() => _lifetime.RunAsync(async token =>
    {
        if (DetailTask is not { } row)
        {
            return;
        }

        try
        {
            ApplyReminderChange(row, await Reminders.ClearSnoozeAsync(row.Id, token));
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            ErrorMessage = "Could not clear the Snooze: " + ex.Message;
        }
    });

    /// <summary>
    /// Snoozes a Task from its Reminder notification; the planned Reminder is unchanged. A Task deleted since
    /// is ignored.
    /// </summary>
    public Task SnoozeTaskAsync(Guid taskId, TimeSpan duration, CancellationToken cancellationToken = default)
        => _lifetime.RunAsync(async token =>
        {
            TaskItem task;
            try
            {
                task = await Reminders.SnoozeAsync(taskId, duration, token);
            }
            catch (KeyNotFoundException)
            {
                return;
            }

            if (_all.Find(r => r.Id == taskId) is { } row)
            {
                ApplyReminderChange(row, task);
            }
        }, cancellationToken);

    private void ApplyReminderChange(TaskRowViewModel row, TaskItem task)
    {
        row.Apply(task);
        if (DetailTask == row)
        {
            LoadReminderRows(task);
            LoadSnoozeDetail(task);
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
            ClearSearchText();
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

        OpenDetailPane(row);
    }

    /// <summary>Completes a Task from its Reminder notification. A Task deleted since is ignored.</summary>
    public Task CompleteTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
        => _lifetime.RunAsync(async token =>
        {
            try
            {
                await taskService.SetCompletedAsync(taskId, true, token);
            }
            catch (KeyNotFoundException)
            {
                return;
            }

            await LoadAsync(token); // also shows a next Occurrence the completion produced
            OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty);
        }, cancellationToken);
}
