using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Lists;
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
    IShellSettingsStore? settingsStore = null,
    IListService? listService = null) : ObservableObject
{
    /// <summary>How long text edits wait for further typing before they are saved.</summary>
    public static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);

    /// <summary>How long the "Task deleted" bar stays visible.</summary>
    public static readonly TimeSpan UndoBarDuration = TimeSpan.FromSeconds(10);

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

    // The Lists by id, for each row's stripe and caption; loaded with every view.
    private Dictionary<Guid, TaskList> _lists = [];

    /// <summary>
    /// How long a Task that was just completed stays in place, struck through and dimmed, before it moves into
    /// "Completed (N)". Evaluated per completion so the UI can answer zero under Windows' reduced-motion
    /// setting. The save and the undo entry never wait for it.
    /// </summary>
    public Func<TimeSpan> CompletionMoveDelay { get; set; } = () => TimeSpan.Zero;

    private Task _completionMove = Task.CompletedTask;

    /// <summary>Completes when the last delayed move into "Completed (N)" has happened.</summary>
    public Task CompletionMove => _completionMove;

    // The rows of Smart Views, a Tag's page and search results caption their Task with the List's name.
    private bool ShowsListNames => CurrentView is not null || TagFilter is not null || IsFilterActive;

    private TaskRowViewModel CreateRow(TaskItem task) =>
        new(task, _clock, _lists.GetValueOrDefault(task.ListId), ShowsListNames);

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

    /// <summary>Raised by Ctrl+, so the shell can open the Settings window (the tray menu's Settings does the same).</summary>
    public event EventHandler? SettingsRequested;

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

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
    partial void OnTagFilterChanged(Tag? value)
    {
        UpdateReorderEnabled();
        NotifyPageStateChanged();
    }

    partial void OnCurrentViewChanged(SmartView? value)
    {
        UpdateReorderEnabled();
        NotifyPageStateChanged();
    }

    private void UpdateReorderEnabled() =>
        IsReorderEnabled = TagFilter is null && CurrentView is null && !IsFilterActive;

    // Loads may overlap (a reload after a Task change while a search reload runs). Each load takes a
    // version; only the latest one applies its results, so Tags are never added twice and an older query
    // that answers last never replaces the newer one's results.
    private int _loadVersion;
    private int _loadsInFlight;
    private Guid? _reselectId;
    private Guid? _reselectDetailId;
    private bool _reselectPaneOpen;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var version = ++_loadVersion;
        if (_loadsInFlight++ == 0)
        {
            // Remember the selection once: a load that overlaps another sees it already cleared.
            _reselectId = SelectedTask?.Id;
            _reselectDetailId = DetailTask?.Id;
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
        SelectedTask = null;
        DetailTask = null; // flushes a pending detail edit of the old row
        await _pendingSave;

        var tags = await tagService.ListTagsAsync(cancellationToken);
        var lists = listService is null ? [] : await listService.GetListsAsync(cancellationToken);
        if (version != _loadVersion)
        {
            return; // a later load applies its own results
        }

        _lists = lists.ToDictionary(s => s.List.Id, s => s.List);
        if (lists.FirstOrDefault(s => s.List.IsDefault) is { } defaultList)
        {
            DefaultListName = defaultList.List.Name; // the heading, quick-add label and empty state name it
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
            var row = CreateRow(task);
            _all.Add(row);
            GroupOf(row).Add(row);
        }

        SortCompleted();
        NotifyCompletedChanged();

        // Reselect the same Task without reopening a detail pane the owner closed.
        SelectedTask = _all.Find(r => r.Id == _reselectId);
        if (!IsDetailDocked)
        {
            DetailTask = _all.Find(r => r.Id == _reselectDetailId); // overlaid: the pane keeps its own Task
        }

        IsDetailPaneOpen = _reselectPaneOpen;
        _loadedToday = DueCalculator.LocalToday(_clock.GetUtcNow(), _clock.LocalTimeZone);
        ScheduleDueRefresh();
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
            var row = CreateRow(task);
            _all.Add(row);
            Tasks.Add(row);
            NotifyPageStateChanged();
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
        var delay = isCompleted ? CompletionMoveDelay() : TimeSpan.Zero;
        if (delay > TimeSpan.Zero)
        {
            // The row shows struck through and dimmed in place; the move follows without holding up the caller.
            _completionMove = MoveAfterAsync(row, delay);
        }
        else
        {
            Place(row);
        }

        OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task MoveAfterAsync(TaskRowViewModel row, TimeSpan delay)
    {
        await Task.Delay(delay, _clock, CancellationToken.None);
        // Undone, deleted or reloaded meanwhile: whoever did that has placed the row already.
        if (row.IsCompleted && _all.Contains(row) && !CompletedTasks.Contains(row))
        {
            Place(row);
        }
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
        NotifyPageStateChanged();
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
