using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

// The detail pane of the selected Task: title, notes, Priority and Due, saved after a debounce.
public sealed partial class MainViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveTagCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddSubtaskCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(IsDetailPaneVisible))]
    private TaskRowViewModel? _selectedTask;

    public bool HasSelection => SelectedTask is not null;

    /// <summary>The pane shows when it is open and a Task is selected.</summary>
    public bool IsDetailPaneVisible => IsDetailPaneOpen && HasSelection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailPaneVisible))]
    private bool _isDetailPaneOpen = true;

    [ObservableProperty]
    private string _detailTitle = "";

    [ObservableProperty]
    private string _detailNotes = "";

    [ObservableProperty]
    private Priority _detailPriority;

    /// <summary>The Due date of the selected Task (a calendar date); null when it has none.</summary>
    [ObservableProperty]
    private DateTime? _detailDueDate;

    /// <summary>The Due time of the selected Task as "H:mm" text; empty when it has none.</summary>
    [ObservableProperty]
    private string _detailDueTime = "";

    /// <summary>Completes when every save started so far has finished.</summary>
    public Task PendingSave => _pendingSave;

    partial void OnSelectedTaskChanging(TaskRowViewModel? value) => FlushPendingEdit();

    partial void OnSelectedTaskChanged(TaskRowViewModel? value)
    {
        _loadingDetail = true;
        DetailTitle = value?.Title ?? "";
        DetailNotes = value?.Item.Notes ?? "";
        DetailPriority = value?.Priority ?? Priority.None;
        DetailDueDate = value?.Item.DueDate?.ToDateTime(TimeOnly.MinValue);
        DetailDueTime = value?.Item.DueTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
        LoadReminderDetail(value?.Item);
        _loadingDetail = false;
        Repeat.Load(value?.Item.Recurrence, value?.Item.DueDate);
        RebuildSubtaskItems();
        if (value is not null)
        {
            IsDetailPaneOpen = true;
        }
    }

    partial void OnDetailTitleChanged(string value) => ScheduleSave(immediate: false);

    partial void OnDetailNotesChanged(string value) => ScheduleSave(immediate: false);

    partial void OnDetailPriorityChanged(Priority value) => ScheduleSave(immediate: true);

    partial void OnDetailDueDateChanged(DateTime? value)
    {
        if (_loadingDetail)
        {
            return;
        }

        if (value is null && DetailDueTime.Length > 0)
        {
            // A Due time cannot outlive its Due date.
            _loadingDetail = true;
            DetailDueTime = "";
            _loadingDetail = false;
        }

        ScheduleDueSave();
    }

    partial void OnDetailDueTimeChanged(string value) => ScheduleDueSave();

    private void ScheduleDueSave()
    {
        if (_loadingDetail || SelectedTask is not { } row)
        {
            return;
        }

        DateOnly? date = DetailDueDate is { } picked ? DateOnly.FromDateTime(picked) : null;
        TimeOnly? time = null;
        var text = DetailDueTime?.Trim() ?? "";
        if (text.Length > 0)
        {
            if (!TimeOnly.TryParseExact(
                    text, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                ErrorMessage = "Enter the Due time as HH:mm, for example 14:30.";
                return;
            }

            time = parsed;
        }

        if (time is not null && date is null)
        {
            ErrorMessage = "A Due time needs a Due date.";
            return;
        }

        ErrorMessage = null;
        _pendingSave = SaveDueAsync(_pendingSave, row, date, time);
    }

    private async Task SaveDueAsync(Task previous, TaskRowViewModel row, DateOnly? date, TimeOnly? time)
    {
        await previous; // saves of one Task never overlap
        try
        {
            var updated = await Due.SetDueAsync(row.Id, date, time);
            row.Apply(updated);
            if (row == SelectedTask && updated.Recurrence is null)
            {
                Repeat.Load(null, updated.DueDate); // the Repeat defaults follow the new Due date
            }

            if (InOverdueGroup(row) != OverdueTasks.Contains(row))
            {
                Place(row);
            }

            ScheduleDueRefresh();
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = "Could not save the changes: " + ex.Message;
        }
    }

    [RelayCommand]
    private void CloseDetailPane()
    {
        FlushPendingEdit();
        IsDetailPaneOpen = false;
    }

    /// <summary>Saves any debounced edit now and waits for all saves; call before the app exits.</summary>
    public Task FlushPendingSaveAsync()
    {
        FlushPendingEdit();
        return _pendingSave;
    }

    /// <summary>
    /// Call before an import: saves any pending detail edit, waits for every save in flight, and closes the
    /// detail pane, so no edit of the old data can land on the imported data. Reload after the import.
    /// </summary>
    public async Task PrepareForImportAsync()
    {
        FlushPendingEdit();
        await _pendingSave;
        SelectedTask = null;
        IsDetailPaneOpen = false;
        await _pendingSave;
    }

    private void FlushPendingEdit()
    {
        if (_debounce is not null && SelectedTask is { } row)
        {
            ScheduleSave(row, immediate: true);
        }
    }

    private void ScheduleSave(bool immediate)
    {
        if (!_loadingDetail && SelectedTask is { } row)
        {
            ScheduleSave(row, immediate);
        }
    }

    private void ScheduleSave(TaskRowViewModel row, bool immediate)
    {
        _debounce?.Cancel();
        _debounce = null;
        var token = CancellationToken.None;
        if (!immediate)
        {
            _debounce = new CancellationTokenSource();
            token = _debounce.Token;
        }

        // Capture now: the fields describe `row` only until the selection changes.
        _pendingSave = SaveDetailAsync(
            _pendingSave, row, DetailTitle, DetailNotes, DetailPriority, immediate, token);
    }

    private async Task SaveDetailAsync(
        Task previous, TaskRowViewModel row, string title, string notes, Priority priority,
        bool immediate, CancellationToken token)
    {
        if (!immediate)
        {
            // Start the delay before awaiting anything, so the timer exists as soon as the edit is made.
            try
            {
                await Task.Delay(SaveDebounce, _clock, token);
            }
            catch (OperationCanceledException)
            {
                return; // superseded by a later edit, a flush or a delete
            }

            _debounce = null;
        }

        await previous; // saves of one Task never overlap

        if (!TaskTitle.IsValid(title) || !TaskNotes.IsValid(notes))
        {
            return; // keep the last valid saved state
        }

        try
        {
            var updated = await taskService.UpdateTaskAsync(row.Id, title, notes, priority);
            row.Apply(updated);
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = "Could not save the changes: " + ex.Message;
        }
    }
}
