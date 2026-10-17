using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

public sealed partial class MainViewModel(
    ITaskService taskService,
    ITagService tagService,
    TimeProvider? timeProvider = null,
    IDueService? dueService = null,
    ITaskSearchService? searchService = null,
    IRecurrenceService? recurrenceService = null,
    IOccurrenceDialogs? occurrenceDialogs = null,
    IReminderService? reminderService = null,
    IShellSettingsStore? settingsStore = null) : ObservableObject
{
    /// <summary>How long text edits wait for further typing before they are saved.</summary>
    public static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);

    /// <summary>How long the "Task deleted" bar stays visible.</summary>
    public static readonly TimeSpan UndoBarDuration = TimeSpan.FromSeconds(10);

    public const string UndoBarMessage = "Task deleted — Undo";
    public const string SubtaskUndoBarMessage = "Subtask deleted — Undo";

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    // Needed for Due edits and Smart Views only.
    private IDueService Due => dueService
        ?? throw new InvalidOperationException("No Due service was supplied.");

    // Needed for search and filters only.
    private ITaskSearchService Search => searchService
        ?? throw new InvalidOperationException("No search service was supplied.");

    // Needed for recurring Tasks only.
    private IRecurrenceService Recurrences => recurrenceService
        ?? throw new InvalidOperationException("No Recurrence service was supplied.");

    private IOccurrenceDialogs OccurrenceDialogs => occurrenceDialogs
        ?? throw new InvalidOperationException("No Occurrence dialogs were supplied.");

    // Needed for Reminder edits only.
    private IReminderService Reminders => reminderService
        ?? throw new InvalidOperationException("No Reminder service was supplied.");

    private int _completedFetched;
    private bool _completedHasMore;

    // Every Task of the current view in manual order, completed ones included.
    private readonly List<TaskRowViewModel> _all = [];
    private UndoEntry? _lastUndo;
    private CancellationTokenSource? _barTimer;
    private CancellationTokenSource? _debounce;
    private bool _loadingDetail;
    private Task _pendingSave = Task.CompletedTask;

    /// <summary>
    /// Raised when a Task was added, completed, un-completed, deleted or restored, so the open-Task
    /// counts of the Lists may have changed.
    /// </summary>
    public event EventHandler? OpenTaskCountsChanged;

    /// <summary>The open Tasks, in manual order.</summary>
    public ObservableCollection<TaskRowViewModel> Tasks { get; } = [];

    /// <summary>The completed Tasks (the "Completed (N)" group), most recently completed first.</summary>
    public ObservableCollection<TaskRowViewModel> CompletedTasks { get; } = [];

    /// <summary>
    /// The Overdue group at the top of the Today view (Overdue Tasks per <see cref="DueCalculator"/>);
    /// empty in every other view.
    /// </summary>
    public ObservableCollection<TaskRowViewModel> OverdueTasks { get; } = [];

    public bool HasOverdueTasks => OverdueTasks.Count > 0;

    public string OverdueHeader => $"Overdue ({OverdueTasks.Count})";

    public bool HasCompletedTasks => CompletedTasks.Count > 0;

    public string CompletedHeader => $"Completed ({CompletedTasks.Count})";

    [ObservableProperty]
    private bool _isCompletedExpanded;

    public ObservableCollection<Tag> Tags { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    private string _quickAddTitle = "";

    /// <summary>The last failure to show to the owner, or null.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>The Tag the Task list is filtered by, or null to show the Default List.</summary>
    [ObservableProperty]
    private Tag? _tagFilter;

    /// <summary>The Smart View being shown, or null for the Default List / Tag filter.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoreCompleted))]
    private SmartView? _currentView;

    // Positions in a Tag-filtered view or a Smart View are not List positions, so reordering is off there.
    partial void OnTagFilterChanged(Tag? value) => UpdateReorderEnabled();

    partial void OnCurrentViewChanged(SmartView? value) => UpdateReorderEnabled();

    private void UpdateReorderEnabled() =>
        IsReorderEnabled = TagFilter is null && CurrentView is null && !IsFilterActive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    private string _newTagName = "";

    // Loads may overlap (a reload after a Task change while a search reload runs). Each load takes a
    // version; only the latest one applies its results, so Tags are never added twice and an older query
    // that answers last never replaces the newer one's results.
    private int _loadVersion;
    private int _loadsInFlight;
    private Guid? _reselectId;
    private bool _reselectPaneOpen;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var version = ++_loadVersion;
        if (_loadsInFlight++ == 0)
        {
            // Remember the selection once: a load that overlaps another sees it already cleared.
            _reselectId = SelectedTask?.Id;
            _reselectPaneOpen = IsDetailPaneOpen;
        }

        try
        {
            await LoadLatestAsync(version, cancellationToken);
        }
        finally
        {
            _loadsInFlight--;
        }
    }

    private async Task LoadLatestAsync(int version, CancellationToken cancellationToken)
    {
        SelectedTask = null; // flushes a pending detail edit of the old row
        await _pendingSave;

        var tags = await tagService.ListTagsAsync(cancellationToken);
        if (version != _loadVersion)
        {
            return; // a later load applies its own results
        }

        Tags.Clear();
        foreach (var tag in tags)
        {
            Tags.Add(tag);
        }

        if (TagFilter is not null)
        {
            // Keep the filter on the current instance of its Tag, or drop it if the Tag was deleted.
            var filterId = TagFilter.Id;
            TagFilter = Tags.FirstOrDefault(t => t.Id == filterId);
        }

        PruneFilterTags();
        RebuildFilterTagChips();

        var tasks = IsFilterActive
            ? await Search.SearchAsync(BuildSearchQuery(0), cancellationToken)
            : CurrentView is { } view
            ? await Due.ListSmartViewAsync(view, 0, cancellationToken)
            : TagFilter is null
                ? await taskService.ListDefaultListTasksAsync(cancellationToken)
                : await tagService.ListTasksByTagAsync(TagFilter.Id, cancellationToken);
        if (version != _loadVersion)
        {
            return;
        }

        _all.Clear();
        Tasks.Clear();
        OverdueTasks.Clear();
        CompletedTasks.Clear();
        _completedFetched = CurrentView == SmartView.Completed ? tasks.Count : 0;
        _completedHasMore = CurrentView == SmartView.Completed && tasks.Count == IDueService.CompletedPageSize;
        foreach (var task in tasks)
        {
            var row = new TaskRowViewModel(task, _clock);
            _all.Add(row);
            GroupOf(row).Add(row);
        }

        SortCompleted();
        NotifyCompletedChanged();

        // Reselect the same Task without reopening a detail pane the owner closed.
        SelectedTask = _all.Find(r => r.Id == _reselectId);
        IsDetailPaneOpen = _reselectPaneOpen;
        _loadedToday = DueCalculator.LocalToday(_clock.GetUtcNow(), _clock.LocalTimeZone);
        ScheduleDueRefresh();
    }

    /// <summary>Filters Tasks by the Tag, or shows the Default List again when null.</summary>
    public async Task FilterByTagAsync(Tag? tag, CancellationToken cancellationToken = default)
    {
        CurrentView = null;
        TagFilter = tag;
        await LoadAsync(cancellationToken);
    }

    /// <summary>
    /// Shows a Smart View across all Lists, or the Default List again when null. Smart Views are not
    /// Tag-filtered, and they turn reordering off.
    /// </summary>
    public async Task ShowSmartViewAsync(SmartView? view, CancellationToken cancellationToken = default)
    {
        TagFilter = null;
        CurrentView = view;
        if (view == SmartView.Completed)
        {
            IsCompletedExpanded = true;
        }

        await LoadAsync(cancellationToken);
    }

    /// <summary>Switches to a Smart View (Ctrl+1 to Ctrl+5).</summary>
    [RelayCommand]
    private Task SelectSmartViewAsync(SmartView view, CancellationToken cancellationToken)
        => ShowSmartViewAsync(view, cancellationToken);

    /// <summary>True when the Completed view may have more Tasks than the pages loaded so far.</summary>
    public bool HasMoreCompleted => CurrentView == SmartView.Completed && _completedHasMore;

    /// <summary>Loads the next page of 100 in the Completed view.</summary>
    [RelayCommand]
    private async Task LoadMoreCompletedAsync(CancellationToken cancellationToken)
    {
        if (CurrentView != SmartView.Completed)
        {
            return;
        }

        var page = IsFilterActive
            ? await Search.SearchAsync(BuildSearchQuery(_completedFetched), cancellationToken)
            : await Due.ListSmartViewAsync(SmartView.Completed, _completedFetched, cancellationToken);
        _completedFetched += page.Count;
        _completedHasMore = page.Count == IDueService.CompletedPageSize;
        foreach (var task in page)
        {
            var row = new TaskRowViewModel(task, _clock);
            _all.Add(row);
            CompletedTasks.Add(row);
        }

        SortCompleted();
        NotifyCompletedChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private async Task AddTaskAsync(CancellationToken cancellationToken)
    {
        if (!CanAddTask())
        {
            return; // ExecuteAsync does not honour CanExecute, so guard here too.
        }

        // In a Smart View the Task goes to the Default List; in Today it is also due today.
        var task = await taskService.CreateTaskAsync(QuickAddTitle, cancellationToken);
        if (CurrentView is { } view)
        {
            if (view == SmartView.Today)
            {
                await Due.SetDueAsync(
                    task.Id, DueCalculator.LocalToday(_clock.GetUtcNow(), _clock.LocalTimeZone), null,
                    cancellationToken);
            }

            await LoadAsync(cancellationToken);
        }
        else if (IsFilterActive)
        {
            // The new Task may not match the search or the filters, so let them decide.
            await LoadAsync(cancellationToken);
        }
        else if (TagFilter is null)
        {
            // A new Task has no Tags, so it never belongs in a Tag-filtered view.
            var row = new TaskRowViewModel(task, _clock);
            _all.Add(row);
            Tasks.Add(row);
        }

        QuickAddTitle = "";
        OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Whether drag-and-drop reordering is allowed. True in a List view; Smart Views and search
    /// results must set it to false.
    /// </summary>
    [ObservableProperty]
    private bool _isReorderEnabled = true;

    /// <summary>Moves the open Task at <paramref name="oldIndex"/> to <paramref name="newIndex"/> and persists it.</summary>
    public async Task MoveTaskAsync(int oldIndex, int newIndex, CancellationToken cancellationToken = default)
    {
        if (!IsReorderEnabled || oldIndex == newIndex
            || oldIndex < 0 || oldIndex >= Tasks.Count
            || newIndex < 0 || newIndex >= Tasks.Count)
        {
            return;
        }

        var moved = Tasks[oldIndex];
        // The service orders the whole List, completed Tasks included.
        var fullIndex = _all.IndexOf(Tasks[newIndex]);
        await taskService.MoveTaskAsync(moved.Id, fullIndex, cancellationToken);
        _all.Remove(moved);
        _all.Insert(fullIndex, moved);
        Tasks.Move(oldIndex, newIndex);
    }

    private bool CanAddTask() => TaskTitle.IsValid(QuickAddTitle);

    // ---- Tags ----

    [RelayCommand(CanExecute = nameof(CanAddTag))]
    private async Task AddTagAsync(CancellationToken cancellationToken)
    {
        if (!CanAddTag())
        {
            return;
        }

        await tagService.AddTagToTaskAsync(SelectedTask!.Id, NewTagName.Trim(), cancellationToken);
        NewTagName = "";
        await LoadAsync(cancellationToken);
    }

    private bool CanAddTag() => SelectedTask is not null && TagName.IsValid(NewTagName?.Trim());

    [RelayCommand(CanExecute = nameof(CanRemoveTag))]
    private async Task RemoveTagAsync(Tag tag, CancellationToken cancellationToken)
    {
        if (!CanRemoveTag())
        {
            return;
        }

        await tagService.RemoveTagFromTaskAsync(SelectedTask!.Id, tag.Id, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    private bool CanRemoveTag() => SelectedTask is not null;

    /// <summary>Deletes the Tag for good. The view must ask for confirmation first; there is no undo.</summary>
    public async Task DeleteTagAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        await tagService.DeleteTagAsync(tag.Id, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    // ---- Completion ----

    /// <summary>Completes an open Task or un-completes a completed one.</summary>
    [RelayCommand]
    private Task ToggleCompleteAsync(TaskRowViewModel? row, CancellationToken cancellationToken)
        => row is null ? Task.CompletedTask : ToggleAsync(row, cancellationToken);

    /// <summary>Toggles the selected Task (the Space shortcut).</summary>
    [RelayCommand]
    private Task ToggleSelectedCompleteAsync(CancellationToken cancellationToken)
        => SelectedTask is null ? Task.CompletedTask : ToggleAsync(SelectedTask, cancellationToken);

    private async Task ToggleAsync(TaskRowViewModel row, CancellationToken cancellationToken)
    {
        var previous = row.IsCompleted;
        var hadNext = row.Item.NextOccurrenceId is not null;
        await SetCompletedAsync(row, !previous, cancellationToken);

        // Completing an Occurrence may have produced the next one; Ctrl+Z right after deletes it again.
        var produced = !previous && !hadNext ? row.Item.NextOccurrenceId : null;
        PushUndo(new ToggleUndo(row.Id, previous, produced));
        if (produced is not null)
        {
            await LoadAsync(cancellationToken);
        }
    }

    private async Task SetCompletedAsync(TaskRowViewModel row, bool isCompleted, CancellationToken cancellationToken)
    {
        var updated = await taskService.SetCompletedAsync(row.Id, isCompleted, cancellationToken);
        row.Apply(updated);
        Place(row);
        OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Delete and undo ----

    /// <summary>Deletes a Task immediately and offers Undo for <see cref="UndoBarDuration"/>.</summary>
    [RelayCommand]
    private Task DeleteAsync(TaskRowViewModel? row, CancellationToken cancellationToken)
        => row is null ? Task.CompletedTask : DeleteRowAsync(row, cancellationToken);

    [RelayCommand]
    private Task DeleteSelectedAsync(CancellationToken cancellationToken)
        => SelectedTask is null ? Task.CompletedTask : DeleteRowAsync(SelectedTask, cancellationToken);

    private async Task DeleteRowAsync(TaskRowViewModel row, CancellationToken cancellationToken)
    {
        // Deleting an Occurrence asks whether to Skip it or stop repeating (SPEC 5.2).
        var choice = row.Item.Recurrence is null
            ? OccurrenceDeleteChoice.StopRepeating
            : OccurrenceDialogs.AskDeleteOccurrence(row.Title);
        if (choice == OccurrenceDeleteChoice.Cancel)
        {
            return;
        }

        _debounce?.Cancel();
        _debounce = null;
        if (choice == OccurrenceDeleteChoice.DeleteThisOccurrence)
        {
            var skipped = await Recurrences.SkipOccurrenceAsync(row.Id, cancellationToken);
            RemoveRow(row);
            PushUndo(new SkipUndo(skipped));
            ShowUndoBar("Task deleted");
            if (skipped.ProducedOccurrenceId is not null)
            {
                await LoadAsync(cancellationToken);
            }

            return;
        }

        var snapshot = await taskService.DeleteTaskAsync(row.Id, cancellationToken);
        RemoveRow(row);
        PushUndo(new DeleteUndo(snapshot));
        ShowUndoBar("Task deleted");
    }

    private void RemoveRow(TaskRowViewModel row)
    {
        if (SelectedTask == row)
        {
            SelectedTask = null;
        }

        _all.Remove(row);
        Tasks.Remove(row);
        OverdueTasks.Remove(row);
        CompletedTasks.Remove(row);
        NotifyCompletedChanged();
        OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty);
    }

    [ObservableProperty]
    private bool _isUndoBarVisible;

    /// <summary>Undoes the last delete or completion toggle (Ctrl+Z and the Undo bar).</summary>
    [RelayCommand]
    private async Task UndoAsync(CancellationToken cancellationToken)
    {
        var entry = _lastUndo;
        if (entry is null)
        {
            return;
        }

        _lastUndo = null;
        HideUndoBar();
        switch (entry)
        {
            case ToggleUndo { ProducedOccurrenceId: { } produced } toggle:
                await Recurrences.UndoCompletionAsync(toggle.TaskId, produced, cancellationToken);
                await LoadAsync(cancellationToken);
                OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case SkipUndo skip:
                await Recurrences.UndoSkipAsync(skip.Skipped, cancellationToken);
                await LoadAsync(cancellationToken);
                OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case ToggleUndo toggle when _all.Find(r => r.Id == toggle.TaskId) is { } row:
                await SetCompletedAsync(row, toggle.PreviousState, cancellationToken);
                break;
            case SubtaskDeleteUndo subtask:
                await RestoreSubtaskAsync(subtask.Snapshot, cancellationToken);
                break;
            case DeleteUndo delete:
                var restored = await taskService.RestoreTaskAsync(delete.Snapshot, cancellationToken);
                var restoredRow = new TaskRowViewModel(restored, _clock);
                var at = _all.FindIndex(r => r.Item.SortOrder > restored.SortOrder);
                _all.Insert(at < 0 ? _all.Count : at, restoredRow);
                Place(restoredRow);
                OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void PushUndo(UndoEntry entry)
    {
        _lastUndo = entry;
        if (entry is not (DeleteUndo or SubtaskDeleteUndo or SkipUndo))
        {
            HideUndoBar();
        }
    }

    /// <summary>What the Undo bar says before its Undo button.</summary>
    [ObservableProperty]
    private string _undoBarText = "Task deleted";

    private void ShowUndoBar(string text)
    {
        UndoBarText = text;
        _barTimer?.Cancel();
        _barTimer = new CancellationTokenSource();
        IsUndoBarVisible = true;
        UndoBarExpiry = ExpireUndoBarAsync(_barTimer.Token);
    }

    private void HideUndoBar()
    {
        _barTimer?.Cancel();
        _barTimer = null;
        IsUndoBarVisible = false;
    }

    /// <summary>Completes when the current Undo bar has timed out or been dismissed.</summary>
    public Task UndoBarExpiry { get; private set; } = Task.CompletedTask;

    private async Task ExpireUndoBarAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(UndoBarDuration, _clock, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // The bar goes away; Ctrl+Z can still undo the delete until another action replaces it.
        IsUndoBarVisible = false;
    }

    private abstract record UndoEntry;

    // ProducedOccurrenceId: the next Occurrence this completion produced, deleted again by the undo.
    private sealed record ToggleUndo(Guid TaskId, bool PreviousState, Guid? ProducedOccurrenceId) : UndoEntry;

    private sealed record SkipUndo(SkippedOccurrence Skipped) : UndoEntry;

    private sealed record DeleteUndo(DeletedTask Snapshot) : UndoEntry;

    private sealed record SubtaskDeleteUndo(Subtask Snapshot) : UndoEntry;

    // ---- Detail pane ----

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

    // ---- Repeat ----

    private RepeatEditorViewModel? _repeat;

    /// <summary>The Repeat section of the detail pane, showing the selected Task's Recurrence.</summary>
    public RepeatEditorViewModel Repeat => _repeat ??= new RepeatEditorViewModel(OnRepeatEdited);

    private void OnRepeatEdited()
    {
        if (SelectedTask is not { } row)
        {
            return;
        }

        if (Repeat.IsRepeating && DetailDueDate is null)
        {
            ErrorMessage = "A repeating Task needs a Due date.";
            return;
        }

        Recurrence? recurrence;
        try
        {
            recurrence = Repeat.Build();
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        ErrorMessage = null;
        _pendingSave = SaveRecurrenceAsync(_pendingSave, row, recurrence);
    }

    private async Task SaveRecurrenceAsync(Task previous, TaskRowViewModel row, Recurrence? recurrence)
    {
        await previous; // saves of one Task never overlap
        try
        {
            var updated = await Recurrences.SetRecurrenceAsync(row.Id, recurrence);
            row.Apply(updated);
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = "Could not save the changes: " + ex.Message;
        }
    }

    // ---- Subtasks ----

    /// <summary>The Subtasks of the selected Task, in their manual order.</summary>
    public ObservableCollection<SubtaskRowViewModel> SubtaskItems { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSubtaskCommand))]
    private string _newSubtaskTitle = "";

    private bool CanAddSubtask() => SelectedTask is not null && TaskTitle.IsValid(NewSubtaskTitle);

    [RelayCommand(CanExecute = nameof(CanAddSubtask))]
    private async Task AddSubtaskAsync(CancellationToken cancellationToken)
    {
        if (!CanAddSubtask())
        {
            return;
        }

        var row = SelectedTask!;
        var subtask = await taskService.AddSubtaskAsync(row.Id, NewSubtaskTitle, cancellationToken);
        row.Subtasks.Add(subtask);
        row.RefreshSubtaskProgress();
        NewSubtaskTitle = "";
        if (row == SelectedTask)
        {
            RebuildSubtaskItems();
        }
    }

    [RelayCommand]
    private async Task DeleteSubtaskAsync(SubtaskRowViewModel? subtask, CancellationToken cancellationToken)
    {
        if (subtask is null || SelectedTask is not { } row)
        {
            return;
        }

        await _pendingSave; // a rename or tick still in flight must land before the Subtask goes
        var deleted = await taskService.DeleteSubtaskAsync(subtask.Id, cancellationToken);
        row.Subtasks.RemoveAll(s => s.Id == deleted.Id);
        row.RefreshSubtaskProgress();
        if (row == SelectedTask)
        {
            RebuildSubtaskItems();
        }

        PushUndo(new SubtaskDeleteUndo(deleted));
        ShowUndoBar("Subtask deleted");
    }

    private async Task RestoreSubtaskAsync(Subtask snapshot, CancellationToken cancellationToken)
    {
        var restored = await taskService.RestoreSubtaskAsync(snapshot, cancellationToken);
        if (_all.Find(r => r.Id == restored.TaskId) is not { } row)
        {
            return; // its Task is not in the current view
        }

        var at = row.Subtasks.FindIndex(s => s.SortOrder > restored.SortOrder);
        row.Subtasks.Insert(at < 0 ? row.Subtasks.Count : at, restored);
        row.RefreshSubtaskProgress();
        if (row == SelectedTask)
        {
            RebuildSubtaskItems();
        }
    }

    [RelayCommand]
    private Task MoveSubtaskUpAsync(SubtaskRowViewModel? subtask, CancellationToken cancellationToken)
        => MoveSubtaskAsync(subtask, -1, cancellationToken);

    [RelayCommand]
    private Task MoveSubtaskDownAsync(SubtaskRowViewModel? subtask, CancellationToken cancellationToken)
        => MoveSubtaskAsync(subtask, 1, cancellationToken);

    private async Task MoveSubtaskAsync(SubtaskRowViewModel? subtask, int offset, CancellationToken cancellationToken)
    {
        if (subtask is null || SelectedTask is not { } row)
        {
            return;
        }

        var newIndex = SubtaskItems.IndexOf(subtask) + offset;
        if (newIndex < 0 || newIndex >= SubtaskItems.Count)
        {
            return;
        }

        await _pendingSave;
        var ordered = await taskService.MoveSubtaskAsync(subtask.Id, newIndex, cancellationToken);
        row.Subtasks.Clear();
        row.Subtasks.AddRange(ordered);
        if (row == SelectedTask)
        {
            RebuildSubtaskItems();
        }
    }

    private void RebuildSubtaskItems()
    {
        SubtaskItems.Clear();
        if (SelectedTask is { } row)
        {
            foreach (var subtask in row.Subtasks)
            {
                SubtaskItems.Add(new SubtaskRowViewModel(subtask, OnSubtaskEdited));
            }
        }
    }

    // A title edit or a tick made in the pane. Saved in order with the other pending saves.
    private void OnSubtaskEdited(SubtaskRowViewModel subtask, SubtaskEdit edit)
    {
        if (SelectedTask is { } row)
        {
            _pendingSave = SaveSubtaskAsync(_pendingSave, row, subtask, edit, subtask.Title, subtask.IsCompleted);
        }
    }

    private async Task SaveSubtaskAsync(
        Task previous, TaskRowViewModel row, SubtaskRowViewModel subtask, SubtaskEdit edit,
        string title, bool isCompleted)
    {
        await previous;
        var known = row.Subtasks.FindIndex(s => s.Id == subtask.Id);
        if (known < 0)
        {
            return;
        }

        try
        {
            Subtask updated;
            if (edit == SubtaskEdit.Title)
            {
                if (!TaskTitle.IsValid(title))
                {
                    subtask.Apply(row.Subtasks[known]); // keep the last valid saved title
                    return;
                }

                updated = await taskService.RenameSubtaskAsync(subtask.Id, title);
            }
            else
            {
                updated = await taskService.SetSubtaskCompletedAsync(subtask.Id, isCompleted);
            }

            row.Subtasks[known] = updated;
            row.RefreshSubtaskProgress();
            if (subtask.Title == title && subtask.IsCompleted == isCompleted)
            {
                subtask.Apply(updated);
            }

            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = "Could not save the changes: " + ex.Message;
        }
    }

    // ---- Refresh as time passes (Today, Overdue) ----

    private ITimer? _dueRefreshTimer;
    private SynchronizationContext? _uiContext;
    private DateOnly _loadedToday;
    private Task _dueRefresh = Task.CompletedTask;

    /// <summary>Completes when the refresh started by the last local midnight or passed Due time has finished.</summary>
    public Task DueRefresh => _dueRefresh;

    // Arms a one-shot timer for the next instant the Due state of the loaded Tasks can change: local midnight
    // or the next timed Due of today (DueCalculator.NextChange). Every refresh arms the next one.
    private void ScheduleDueRefresh()
    {
        _uiContext ??= SynchronizationContext.Current; // the UI thread in the app; none in tests
        var now = _clock.GetUtcNow();
        var due = DueCalculator.NextChange(_all.Select(r => r.Item), now, _clock.LocalTimeZone) - now;
        _dueRefreshTimer?.Dispose();
        _dueRefreshTimer = _clock.CreateTimer(
            _ => OnDueRefreshTimer(), null, due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
    }

    private void OnDueRefreshTimer()
    {
        var done = new TaskCompletionSource();
        _dueRefresh = done.Task;
#pragma warning disable VSTHRD101 // Async void is intended: the body catches everything and always completes the TCS
        SendOrPostCallback run = async _ =>
        {
            try
            {
                await RefreshDueStateAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ErrorMessage = "Could not refresh the view: " + ex.Message;
            }
            finally
            {
                done.TrySetResult();
            }
        };
#pragma warning restore VSTHRD101

        if (_uiContext is { } context)
        {
            context.Post(run, null);
        }
        else
        {
            run(null);
        }
    }

    private async Task RefreshDueStateAsync()
    {
        var today = DueCalculator.LocalToday(_clock.GetUtcNow(), _clock.LocalTimeZone);
        // Which Tasks a Smart View holds depends on the date (and, for Overdue, on the time): reload it.
        if (CurrentView == SmartView.Overdue
            || (today != _loadedToday && CurrentView is SmartView.Today or SmartView.Upcoming))
        {
            await LoadAsync();
            return;
        }

        foreach (var row in _all)
        {
            var wasOverdueGroup = OverdueTasks.Contains(row);
            row.RefreshOverdue();
            if (!row.IsCompleted && InOverdueGroup(row) != wasOverdueGroup)
            {
                Place(row);
            }
        }

        _loadedToday = today;
        ScheduleDueRefresh();
    }

    // ---- Grouping helpers ----

    // Puts a row whose completion changed (or that was just restored) into the right group and position.
    private void Place(TaskRowViewModel row)
    {
        Tasks.Remove(row);
        OverdueTasks.Remove(row);
        CompletedTasks.Remove(row);
        if (row.IsCompleted)
        {
            CompletedTasks.Add(row);
            SortCompleted();
        }
        else
        {
            var group = GroupOf(row);
            var index = _all.TakeWhile(r => r != row).Count(r => !r.IsCompleted && GroupOf(r) == group);
            group.Insert(index, row);
        }

        NotifyCompletedChanged();
    }

    // Only the Today view has an Overdue group; elsewhere an Overdue Task sits with the others.
    private bool InOverdueGroup(TaskRowViewModel row) => CurrentView == SmartView.Today && row.IsOverdue;

    private ObservableCollection<TaskRowViewModel> GroupOf(TaskRowViewModel row) =>
        row.IsCompleted ? CompletedTasks : InOverdueGroup(row) ? OverdueTasks : Tasks;

    private void NotifyCompletedChanged()
    {
        OnPropertyChanged(nameof(OverdueHeader));
        OnPropertyChanged(nameof(HasOverdueTasks));
        OnPropertyChanged(nameof(HasMoreCompleted));
        OnPropertyChanged(nameof(CompletedHeader));
        OnPropertyChanged(nameof(HasCompletedTasks));
    }

    private void SortCompleted()
    {
        var sorted = CompletedTasks.OrderByDescending(r => r.Item.CompletedUtc).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var from = CompletedTasks.IndexOf(sorted[i]);
            if (from != i)
            {
                CompletedTasks.Move(from, i);
            }
        }
    }
}
