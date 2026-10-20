using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

// The detail pane of the selected Task: title, notes, Priority and Due, saved after a debounce.
public sealed partial class MainViewModel
{
    /// <summary>The Task selected in the list: the target of Space, Delete and the row highlight.</summary>
    [ObservableProperty]
    private TaskRowViewModel? _selectedTask;

    /// <summary>
    /// The Task the detail pane shows and edits. It follows <see cref="SelectedTask"/> while the pane is
    /// docked; while it overlays the list it changes only on <see cref="OpenDetailPane"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveTagCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddSubtaskCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(IsDetailPaneVisible))]
    [NotifyPropertyChangedFor(nameof(HasHeadingTools))]
    [NotifyPropertyChangedFor(nameof(HasDetailDue))]
    private TaskRowViewModel? _detailTask;

    public bool HasSelection => DetailTask is not null;

    /// <summary>The pane shows when it is open and has a Task.</summary>
    public bool IsDetailPaneVisible => IsDetailPaneOpen && HasSelection;

    /// <summary>
    /// Filter and search show in the page heading unless the detail pane overlays the page, which hides them
    /// (Ctrl+F closes the pane first).
    /// </summary>
    public bool HasHeadingTools => !(IsDetailPaneVisible && !IsDetailDocked);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailPaneVisible))]
    [NotifyPropertyChangedFor(nameof(HasHeadingTools))]
    private bool _isDetailPaneOpen = true;

    /// <summary>
    /// True when the view docks the pane beside the list (a wide window); false when it overlays the list.
    /// The view sets it from the window width. Docked, a selection loads the pane (without reopening it);
    /// overlaid, only <see cref="OpenDetailPane"/> does.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeadingTools))]
    private bool _isDetailDocked = true;

    [ObservableProperty]
    private string _detailTitle = "";

    [ObservableProperty]
    private string _detailNotes = "";

    [ObservableProperty]
    private Priority _detailPriority;

    /// <summary>The Due date of the shown Task (a calendar date); null when it has none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetailDue))]
    private DateTime? _detailDueDate;

    /// <summary>Repeat only applies once the Task has a Due date, so the pane hides it until then.</summary>
    public bool HasDetailDue => DetailDueDate is not null;

    /// <summary>
    /// The Due time of the shown Task as a time of day, bindable to a time picker's selected time; null when it
    /// has none (a date-only Due, or no Due).
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearDetailDueTimeCommand))]
    private TimeSpan? _detailDueTime;

    /// <summary>Completes when every save started so far has finished.</summary>
    public Task PendingSave => Saves.WaitForAllAsync();

    private DetailSaveQueue? _saves;

    // Every detail save goes through this queue (see DetailSaveQueue).
    private DetailSaveQueue Saves =>
        _saves ??= new DetailSaveQueue(_lifetime, _clock, SaveDebounce, message => ErrorMessage = message);

    /// <summary>
    /// Opens the pane on a Task (a click or Enter on its row): selects it and shows it, whether the pane is
    /// docked or overlaid. With no row, opens it on the selected Task.
    /// </summary>
    public void OpenDetailPane(TaskRowViewModel? row = null)
    {
        row ??= SelectedTask;
        if (row is null)
        {
            return;
        }

        SelectedTask = row;
        DetailTask = row;
        IsDetailPaneOpen = true;
    }

    // Docked, the pane follows the selection, but a selection never reopens a pane the owner closed:
    // only OpenDetailPane does (a click, Enter, F2 or a notification).
    partial void OnSelectedTaskChanged(TaskRowViewModel? value)
    {
        if (IsDetailDocked)
        {
            DetailTask = value;
        }
    }

    // A pane that overlaid the list and is now docked beside it follows the selection again, including a
    // selection made while it overlaid the list and showed nothing. A closed pane stays closed.
    partial void OnIsDetailDockedChanged(bool value)
    {
        if (value && SelectedTask is not null && SelectedTask != DetailTask)
        {
            DetailTask = SelectedTask;
        }
    }

    partial void OnDetailTaskChanging(TaskRowViewModel? value) => Saves.Flush();

    partial void OnDetailTaskChanged(TaskRowViewModel? value)
    {
        _loadingDetail = true;
        DetailTitle = value?.Title ?? "";
        DetailNotes = value?.Item.Notes ?? "";
        DetailPriority = value?.Priority ?? Priority.None;
        DetailDueDate = value?.Item.DueDate?.ToDateTime(TimeOnly.MinValue);
        DetailDueTime = value?.Item.DueTime?.ToTimeSpan();
        LoadReminderDetail(value?.Item);
        _loadingDetail = false;
        Repeat.Load(value?.Item.Recurrence, value?.Item.DueDate);
        RebuildSubtaskItems();
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

        if (value is null && DetailDueTime is not null)
        {
            // A Due time cannot outlive its Due date.
            _loadingDetail = true;
            DetailDueTime = null;
            _loadingDetail = false;
        }

        ScheduleDueSave();
    }

    partial void OnDetailDueTimeChanged(TimeSpan? value) => ScheduleDueSave();

    /// <summary>Clears the Due time (the pane's "Clear Due time" button): the Due becomes date-only.</summary>
    [RelayCommand(CanExecute = nameof(CanClearDetailDueTime))]
    private void ClearDetailDueTime() => DetailDueTime = null;

    private bool CanClearDetailDueTime() => DetailDueTime is not null;

    private void ScheduleDueSave()
    {
        if (_loadingDetail || DetailTask is not { } row)
        {
            return;
        }

        DateOnly? date = DetailDueDate is { } day ? DateOnly.FromDateTime(day) : null;
        TimeOnly? time = DetailDueTime is { } picked ? TimeOnly.FromTimeSpan(picked) : null;
        if (time is not null && date is null)
        {
            // The pane can't reach this (its time picker needs a date, and clearing the date clears the time);
            // it guards callers that set the time directly.
            ShowFieldError(ErrorPlace.Due, "A Due time needs a Due date.");
            return;
        }

        _ = Saves.Enqueue(() => StoreDueAsync(row, date, time), accepted: () => ErrorMessage = null);
    }

    private async Task<bool> StoreDueAsync(TaskRowViewModel row, DateOnly? date, TimeOnly? time)
    {
        var updated = await Due.SetDueAsync(row.Id, date, time);
        row.Apply(updated);
        if (row == DetailTask && updated.Recurrence is null)
        {
            Repeat.Load(null, updated.DueDate); // the Repeat defaults follow the new Due date
        }

        if (InOverdueGroup(row) != OverdueTasks.Contains(row))
        {
            Place(row);
        }

        ScheduleDueRefresh();
        OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty); // the Smart View counts depend on Due
        return true;
    }

    [RelayCommand]
    private void CloseDetailPane()
    {
        Saves.Flush();
        IsDetailPaneOpen = false;
    }

    /// <summary>Deletes the Task shown in the pane (the pane's "Delete Task" button).</summary>
    [RelayCommand]
    private Task DeleteDetailTaskAsync(CancellationToken cancellationToken)
        => DetailTask is null ? Task.CompletedTask : DeleteRowAsync(DetailTask, cancellationToken);

    /// <summary>Saves any debounced edit now and waits for all saves; call before the app exits.</summary>
    public Task FlushPendingSaveAsync()
    {
        Saves.Flush();
        return Saves.WaitForAllAsync();
    }

    /// <summary>
    /// Call before an import: saves any pending detail edit, waits for every save in flight, and closes the
    /// detail pane, so no edit of the old data can land on the imported data. Reload after the import.
    /// </summary>
    public async Task PrepareForImportAsync()
    {
        await FlushPendingSaveAsync();
        SelectedTask = null;
        DetailTask = null;
        IsDetailPaneOpen = false;
        await Saves.WaitForAllAsync();
    }

    private void ScheduleSave(bool immediate)
    {
        if (_loadingDetail || DetailTask is not { } row)
        {
            return;
        }

        // Capture now: the fields describe `row` only until the selection changes.
        var (title, notes, priority) = (DetailTitle, DetailNotes, DetailPriority);
        Saves.ScheduleEdit(row.Id, () => StoreDetailAsync(row, title, notes, priority), immediate);
    }

    private async Task<bool> StoreDetailAsync(TaskRowViewModel row, string title, string notes, Priority priority)
    {
        if (!TaskTitle.IsValid(title) || !TaskNotes.IsValid(notes))
        {
            return false; // keep the last valid saved state
        }

        row.Apply(await taskService.UpdateTaskAsync(row.Id, title, notes, priority));
        return true;
    }
}
