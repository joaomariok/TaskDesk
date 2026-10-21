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
    private ITimer? _reminderRowTimer;

    // Each armed timer takes a version; a tick that was already posted runs only if no re-arm or stop came after it.
    private int _reminderRowVersion;

    // Set when a live update of the rows waits for the Reminder flyout to close.
    private bool _reminderRowsBehind;

    // The longest wait a timer accepts (about 49.7 days); a moment further away is reached by re-arming on the way.
    private static readonly TimeSpan MaxTimerWait = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>
    /// The selected Task's Reminders by closeness to now: those ahead first, soonest on top, then the past
    /// ones, the most recent first.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<ReminderRowViewModel> DetailReminders { get; } = [];

    /// <summary>
    /// Raised before <see cref="DetailReminders"/> is rebuilt, while the old rows are still listed: a view notes
    /// which row has the focus here, and gives it to that Reminder's new row on <see cref="ReminderRowsRebuilt"/>.
    /// </summary>
    public event EventHandler? ReminderRowsRebuilding;

    /// <summary>Raised once <see cref="DetailReminders"/> has been rebuilt.</summary>
    public event EventHandler? ReminderRowsRebuilt;

    /// <summary>True while a timer waits for the next moment of a Reminder listed in the open pane.</summary>
    public bool IsReminderRowTimerArmed => _reminderRowTimer is not null;

    /// <summary>True while the selected Task has a Reminder: the Reminders box shows only then.</summary>
    public bool HasDetailReminders => DetailReminders.Count > 0;

    /// <summary>The Reminder flyout, opened by "Add reminder" or a click on a row.</summary>
    public ReminderEditorViewModel ReminderEditor => _reminderEditor ??= CreateReminderEditor();

    private ReminderEditorViewModel CreateReminderEditor()
    {
        var editor = new ReminderEditorViewModel(DefaultReminderTime, SaveReminder);
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReminderEditorViewModel.IsOpen) && !editor.IsOpen && _reminderRowsBehind)
            {
                LoadReminderRows(DetailTask?.Item);
            }
        };
        return editor;
    }

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

    // A live update of the rows: a Reminder's moment arriving, or local midnight renaming the days. It rebuilds
    // the rows, unless the Reminder flyout is open: then the rows wait until it closes, so the update never pulls
    // the row the editor came from out from under the user.
    private void RefreshReminderRows()
    {
        if (ReminderEditor.IsOpen)
        {
            _reminderRowsBehind = true;
            StopReminderRowTimer();
            return;
        }

        LoadReminderRows(DetailTask?.Item);
    }

    private void LoadReminderRows(TaskItem? task)
    {
        _reminderRowsBehind = false;
        ReminderRowsRebuilding?.Invoke(this, EventArgs.Empty);
        DetailReminders.Clear();
        if (task is null)
        {
            StopReminderRowTimer();
            OnPropertyChanged(nameof(HasDetailReminders));
            ReminderRowsRebuilt?.Invoke(this, EventArgs.Empty);
            return;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var today = DueCalculator.LocalToday(_clock.GetUtcNow(), _clock.LocalTimeZone);
        var rules = Rules();
        var rows = task.Reminders
            .Select(r => (Reminder: r, At: ReminderCalculator.MomentOf(r, task, rules)))
            .Where(r => r.At is not null)
            .OrderBy(r => r.At <= now) // ahead first, then past
            .ThenBy(r => r.At <= now ? -r.At!.Value.Ticks : r.At!.Value.Ticks) // nearest to now first in each
            .ThenBy(r => r.Reminder.Id);
        foreach (var (reminder, at) in rows)
        {
            var (date, time) = ReminderCalculator.ToLocal(at!.Value, _clock.LocalTimeZone);
            var when = ReminderWording.Moment(date, time, today);
            var (text, wording) = reminder is { Kind: ReminderKind.BeforeDue, Amount: { } amount, Unit: { } unit }
                ? ($"{ReminderWording.CompactOffset(amount, unit)} before · {when}",
                    $"{ReminderWording.FullOffset(amount, unit)} before Due, {when}")
                : (when, when);
            DetailReminders.Add(new ReminderRowViewModel(
                reminder.Id, task.Id, reminder.Value, at.Value, text, wording, at.Value <= now, EditReminder,
                RemoveReminderAsync));
        }

        OnPropertyChanged(nameof(HasDetailReminders));
        ReminderRowsRebuilt?.Invoke(this, EventArgs.Empty);
        ArmReminderRowTimer(DetailReminders.Where(r => !r.IsPast).Select(r => (DateTime?)r.FiresAtUtc).Min());
    }

    // While the pane is open, one timer waits for the next moment of a listed Reminder; its tick rebuilds the
    // rows, so that row takes the past look and moves into the past block, and the rebuild aims the timer at
    // the moment after it. Nothing ticks in between.
    private void ArmReminderRowTimer(DateTime? nextUtc)
    {
        StopReminderRowTimer();
        if (nextUtc is not { } next || !IsDetailPaneOpen || _lifetime.IsStopped)
        {
            return;
        }

        _uiContext ??= SynchronizationContext.Current; // the UI thread in the app; none in tests
        // The clock has moved on since the rows were built, so the moment may have passed already: then tick at once.
        var wait = next - _clock.GetUtcNow().UtcDateTime;
        var version = _reminderRowVersion;
        _reminderRowTimer = _clock.CreateTimer(
            _ => OnUiThread(_ => OnReminderRowTimer(version)), null,
            wait < TimeSpan.Zero ? TimeSpan.Zero : wait > MaxTimerWait ? MaxTimerWait : wait, Timeout.InfiniteTimeSpan);
    }

    private void OnReminderRowTimer(int version)
    {
        if (version != _reminderRowVersion || _lifetime.IsStopped)
        {
            return; // re-armed, stopped, or the view model has stopped since this tick was posted
        }

        RefreshReminderRows();
    }

    private void StopReminderRowTimer()
    {
        _reminderRowVersion++;
        _reminderRowTimer?.Dispose();
        _reminderRowTimer = null;
    }

    // Closed, the pane keeps no timer; reopened, its rows are rebuilt, current as of now, and the timer re-armed.
    partial void OnIsDetailPaneOpenChanged(bool value)
    {
        if (value)
        {
            LoadReminderRows(DetailTask?.Item);
        }
        else
        {
            StopReminderRowTimer();
        }
    }

    // The local rules the Due is read in, read each time, so a Settings change applies without a restart.
    private MomentRules Rules() => new(_clock.LocalTimeZone, DefaultReminderTime());

    // Before Due needs a Due: the pane's Due, which a queued Due save stores before any Reminder save.
    private bool DetailHasDue => DetailDueDate is not null;

    /// <summary>Opens the Reminder flyout on a new Reminder ("Add reminder").</summary>
    [RelayCommand]
    private void AddReminder() => ReminderEditor.Open(null, null, null, null, DetailHasDue);

    private void EditReminder(ReminderRowViewModel row)
    {
        if (row.Value.Kind == ReminderKind.BeforeDue)
        {
            ReminderEditor.Open(row.Id, null, null, row.Value, DetailHasDue);
            return;
        }

        var (date, time) = ReminderCalculator.ToLocal(row.FiresAtUtc, _clock.LocalTimeZone);
        ReminderEditor.Open(row.Id, date.ToDateTime(TimeOnly.MinValue), time.ToTimeSpan(), null, DetailHasDue);
    }

    // The flyout's Save: false keeps it open with the error under Reminders.
    private bool SaveReminder(ReminderEditorViewModel editor)
    {
        if (DetailTask is not { } row)
        {
            return false;
        }

        if (editor.IsBeforeDue)
        {
            return SaveBeforeDueReminder(editor, row);
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
        EnqueueReminderSave(row, editor.EditingId, value);
        return true;
    }

    private bool SaveBeforeDueReminder(ReminderEditorViewModel editor, TaskRowViewModel row)
    {
        if (!DetailHasDue)
        {
            ShowFieldError(ErrorPlace.Reminder, "A Reminder before Due needs a Due.");
            return false;
        }

        if (editor.BeforeDueValue() is not { } value)
        {
            ShowFieldError(
                ErrorPlace.Reminder,
                $"A Reminder before Due needs a whole number from {ReminderValue.MinAmount} to {ReminderValue.MaxAmount}.");
            return false;
        }

        EnqueueReminderSave(row, editor.EditingId, value);
        return true;
    }

    private void EnqueueReminderSave(TaskRowViewModel row, Guid? editingId, ReminderValue value) =>
        _ = Saves.Enqueue(
            () => StoreReminderAsync(row, () => editingId is { } id
                ? Reminders.UpdateReminderAsync(id, value)
                : Reminders.AddReminderAsync(row.Id, value)),
            accepted: () => ErrorMessage = null);

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
