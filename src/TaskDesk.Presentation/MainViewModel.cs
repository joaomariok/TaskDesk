using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

public sealed partial class MainViewModel(
    ITaskService taskService, ITagService tagService, TimeProvider? timeProvider = null) : ObservableObject
{
    /// <summary>How long text edits wait for further typing before they are saved.</summary>
    public static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);

    /// <summary>How long the "Task deleted" bar stays visible.</summary>
    public static readonly TimeSpan UndoBarDuration = TimeSpan.FromSeconds(10);

    public const string UndoBarMessage = "Task deleted — Undo";

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

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

    // Positions in a Tag-filtered view are not List positions, so reordering is off there.
    partial void OnTagFilterChanged(Tag? value) => IsReorderEnabled = value is null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    private string _newTagName = "";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var selectedId = SelectedTask?.Id;
        var paneOpen = IsDetailPaneOpen;
        SelectedTask = null; // flushes a pending detail edit of the old row
        await _pendingSave;

        Tags.Clear();
        foreach (var tag in await tagService.ListTagsAsync(cancellationToken))
        {
            Tags.Add(tag);
        }

        if (TagFilter is not null)
        {
            // Keep the filter on the current instance of its Tag, or drop it if the Tag was deleted.
            var filterId = TagFilter.Id;
            TagFilter = Tags.FirstOrDefault(t => t.Id == filterId);
        }

        var tasks = TagFilter is null
            ? await taskService.ListDefaultListTasksAsync(cancellationToken)
            : await tagService.ListTasksByTagAsync(TagFilter.Id, cancellationToken);
        _all.Clear();
        Tasks.Clear();
        CompletedTasks.Clear();
        foreach (var task in tasks)
        {
            var row = new TaskRowViewModel(task);
            _all.Add(row);
            (task.IsCompleted ? CompletedTasks : Tasks).Add(row);
        }

        SortCompleted();
        NotifyCompletedChanged();

        // Reselect the same Task without reopening a detail pane the owner closed.
        SelectedTask = _all.Find(r => r.Id == selectedId);
        IsDetailPaneOpen = paneOpen;
    }

    /// <summary>Filters Tasks by the Tag, or shows the Default List again when null.</summary>
    public async Task FilterByTagAsync(Tag? tag, CancellationToken cancellationToken = default)
    {
        TagFilter = tag;
        await LoadAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private async Task AddTaskAsync(CancellationToken cancellationToken)
    {
        if (!CanAddTask())
        {
            return; // ExecuteAsync does not honour CanExecute, so guard here too.
        }

        var task = await taskService.CreateTaskAsync(QuickAddTitle, cancellationToken);
        if (TagFilter is null)
        {
            // A new Task has no Tags, so it never belongs in a Tag-filtered view.
            var row = new TaskRowViewModel(task);
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
        await SetCompletedAsync(row, !previous, cancellationToken);
        PushUndo(new ToggleUndo(row.Id, previous));
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
        _debounce?.Cancel();
        _debounce = null;
        var snapshot = await taskService.DeleteTaskAsync(row.Id, cancellationToken);

        if (SelectedTask == row)
        {
            SelectedTask = null;
        }

        _all.Remove(row);
        Tasks.Remove(row);
        CompletedTasks.Remove(row);
        NotifyCompletedChanged();
        OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty);

        PushUndo(new DeleteUndo(snapshot));
        ShowUndoBar();
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
            case ToggleUndo toggle when _all.Find(r => r.Id == toggle.TaskId) is { } row:
                await SetCompletedAsync(row, toggle.PreviousState, cancellationToken);
                break;
            case DeleteUndo delete:
                var restored = await taskService.RestoreTaskAsync(delete.Snapshot, cancellationToken);
                var restoredRow = new TaskRowViewModel(restored);
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
        if (entry is not DeleteUndo)
        {
            HideUndoBar();
        }
    }

    private void ShowUndoBar()
    {
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

    private sealed record ToggleUndo(Guid TaskId, bool PreviousState) : UndoEntry;

    private sealed record DeleteUndo(DeletedTask Snapshot) : UndoEntry;

    // ---- Detail pane ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveTagCommand))]
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

    /// <summary>Completes when every save started so far has finished.</summary>
    public Task PendingSave => _pendingSave;

    partial void OnSelectedTaskChanging(TaskRowViewModel? value) => FlushPendingEdit();

    partial void OnSelectedTaskChanged(TaskRowViewModel? value)
    {
        _loadingDetail = true;
        DetailTitle = value?.Title ?? "";
        DetailNotes = value?.Item.Notes ?? "";
        DetailPriority = value?.Priority ?? Priority.None;
        _loadingDetail = false;
        if (value is not null)
        {
            IsDetailPaneOpen = true;
        }
    }

    partial void OnDetailTitleChanged(string value) => ScheduleSave(immediate: false);

    partial void OnDetailNotesChanged(string value) => ScheduleSave(immediate: false);

    partial void OnDetailPriorityChanged(Priority value) => ScheduleSave(immediate: true);

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

    // ---- Grouping helpers ----

    // Puts a row whose completion changed (or that was just restored) into the right group and position.
    private void Place(TaskRowViewModel row)
    {
        Tasks.Remove(row);
        CompletedTasks.Remove(row);
        if (row.IsCompleted)
        {
            CompletedTasks.Add(row);
            SortCompleted();
        }
        else
        {
            var index = _all.TakeWhile(r => r != row).Count(r => !r.IsCompleted);
            Tasks.Insert(index, row);
        }

        NotifyCompletedChanged();
    }

    private void NotifyCompletedChanged()
    {
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
