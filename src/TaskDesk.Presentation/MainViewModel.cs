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
    TimeProvider timeProvider,
    IDueService? dueService = null,
    ITaskSearchService? searchService = null,
    IRecurrenceService? recurrenceService = null,
    IOccurrenceDialogs? occurrenceDialogs = null,
    IReminderService? reminderService = null,
    IShellSettingsStore? settingsStore = null,
    IListService? listService = null,
    ITaskCreator? taskCreator = null) : ObservableObject
{
    /// <summary>How long text edits wait for further typing before they are saved.</summary>
    public static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);

    /// <summary>How long the "Task deleted" bar stays visible.</summary>
    public static readonly TimeSpan UndoBarDuration = TimeSpan.FromSeconds(10);

    // Required, so a test can never arm a real timer by leaving the clock out.
    private readonly TimeProvider _clock = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

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

    // Needed for quick-add on a List page other than the Default List's only.
    private ITaskCreator Creator => taskCreator
        ?? throw new InvalidOperationException("No Task creator was supplied.");

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
    private bool ShowsListNames => Page is not TaskListPage.ListPage || IsNarrowed;

    private TaskRowViewModel CreateRow(TaskItem task) =>
        new(task, _clock, _lists.GetValueOrDefault(task.ListId), ShowsListNames, IsCompletedPage);

    private int _completedFetched;
    private bool _completedHasMore;

    // Every Task of the current view in manual order, completed ones included.
    private readonly List<TaskRowViewModel> _all = [];
    private UndoEntry? _lastUndo;
    private CancellationTokenSource? _barTimer;
    private bool _loadingDetail;

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

    /// <summary>What the Task list shows; the only page state. Navigate with <see cref="ShowAsync"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentView))]
    [NotifyPropertyChangedFor(nameof(TagFilter))]
    [NotifyPropertyChangedFor(nameof(CurrentListId))]
    [NotifyPropertyChangedFor(nameof(HasMoreCompleted))]
    [NotifyPropertyChangedFor(nameof(ShowsQuickAdd))]
    [NotifyPropertyChangedFor(nameof(QuickAddTarget))]
    private TaskListPage _page = TaskListPage.DefaultList;

    /// <summary>The Smart View being shown, or null on a List's or a Tag's page.</summary>
    public SmartView? CurrentView => (Page as TaskListPage.SmartViewPage)?.View;

    /// <summary>The Tag whose page is shown, or null on a List's page or a Smart View.</summary>
    public Tag? TagFilter => (Page as TaskListPage.TagPage)?.Tag;

    /// <summary>
    /// The List whose page is shown, or null on a Smart View or a Tag's page. Also null for the Default List
    /// until the first load knows its id.
    /// </summary>
    public Guid? CurrentListId => (Page as TaskListPage.ListPage)?.ListId;

    // Positions in a Tag-filtered view or a Smart View are not List positions, so reordering is off there.
    partial void OnPageChanged(TaskListPage value)
    {
        UpdateReorderEnabled();
        NotifyPageStateChanged();
    }

    private void UpdateReorderEnabled() =>
        IsReorderEnabled = Page is TaskListPage.ListPage && !IsNarrowed;

    /// <summary>Shows <paramref name="page"/> and loads its Tasks.</summary>
    public async Task ShowAsync(TaskListPage page, CancellationToken cancellationToken = default)
    {
        Page = page;
        await LoadAsync(cancellationToken);
    }

    /// <summary>Shows a List's page. Opening a List ends the search; Filters stay on.</summary>
    public Task ShowListAsync(Guid listId, CancellationToken cancellationToken = default)
    {
        ClearSearchText();
        return ShowAsync(new TaskListPage.ListPage(listId), cancellationToken);
    }

    // Loads may overlap (a reload after a Task change while a search reload runs). Each load takes a
    // version; only the latest one applies its results, so Tags are never added twice and an older query
    // that answers last never replaces the newer one's results.
    private int _loadVersion;
    private int _loadsInFlight;
    private Guid? _reselectId;
    private Guid? _reselectDetailId;
    private bool _reselectPaneOpen;

    /// <summary>
    /// Loads the current page. Does nothing once the view model has stopped (<see cref="StopAsync"/>), so the
    /// reloads that pass no token (a List edited, a quick-add Task created, an import) do nothing after it too; a
    /// load the stop cancelled ends quietly, also when its database has been let go of meanwhile.
    /// </summary>
    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        _lifetime.RunAsync(LoadInLifetimeAsync, cancellationToken);

    private async Task LoadInLifetimeAsync(CancellationToken cancellationToken)
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
        await Saves.WaitForAllAsync();

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

        if (TagFilter is { } filter)
        {
            // Keep the page on the current instance of its Tag, or go back to the Default List if it was deleted.
            Page = Tags.FirstOrDefault(t => t.Id == filter.Id) is { } tag
                ? new TaskListPage.TagPage(tag)
                : TaskListPage.DefaultList;
        }

        // The Default List's placeholder page, or a List that was deleted, becomes the Default List's page.
        if (Page is TaskListPage.ListPage { ListId: var shownListId }
            && (shownListId is null || !_lists.ContainsKey(shownListId.Value))
            && lists.FirstOrDefault(s => s.List.IsDefault) is { } fallback)
        {
            Page = new TaskListPage.ListPage(fallback.List.Id);
        }

        PruneFilterTags();
        RebuildFilterTagChips();

        var tasks = IsNarrowed
            ? await Search.SearchAsync(BuildSearchQuery(0), cancellationToken)
            : Page switch
            {
                TaskListPage.SmartViewPage page => await Due.ListSmartViewAsync(page.View, 0, cancellationToken),
                TaskListPage.TagPage page => await tagService.ListTasksByTagAsync(page.Tag.Id, cancellationToken),
                TaskListPage.ListPage { ListId: { } listId } =>
                    await taskService.ListTasksInListAsync(listId, cancellationToken),
                _ => await taskService.ListDefaultListTasksAsync(cancellationToken),
            };
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
    private Task AddTaskAsync(CancellationToken cancellationToken) =>
        _lifetime.RunAsync(AddTaskInLifetimeAsync, cancellationToken);

    private async Task AddTaskInLifetimeAsync(CancellationToken cancellationToken)
    {
        if (!CanAddTask())
        {
            return; // ExecuteAsync does not honour CanExecute, so guard here too.
        }

        // On a List's page the Task goes to that List. Elsewhere it goes to the Default List; in Today it is
        // also due today, and on a Tag's page it gets that Tag.
        var task = Page is TaskListPage.ListPage { ListId: { } listId }
            && _lists.TryGetValue(listId, out var list) && !list.IsDefault
            ? await Creator.CreateTaskInListAsync(QuickAddTitle, listId, cancellationToken)
            : await taskService.CreateTaskAsync(QuickAddTitle, cancellationToken);
        if (Page is TaskListPage.TagPage tagPage)
        {
            await tagService.AddTagToTaskAsync(task.Id, tagPage.Tag.Name, cancellationToken);
            await LoadAsync(cancellationToken);
        }
        else if (CurrentView is { } view)
        {
            if (view == SmartView.Today)
            {
                await Due.SetDueAsync(
                    task.Id, DueCalculator.LocalToday(_clock.GetUtcNow(), _clock.LocalTimeZone), null,
                    cancellationToken);
            }

            await LoadAsync(cancellationToken);
        }
        else if (IsNarrowed)
        {
            // The new Task may not match the search or the filters, so let them decide.
            await LoadAsync(cancellationToken);
        }
        else if (Page is TaskListPage.ListPage)
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
    public Task MoveTaskAsync(int oldIndex, int newIndex, CancellationToken cancellationToken = default)
        => _lifetime.RunAsync(async token =>
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
            await taskService.MoveTaskAsync(moved.Id, fullIndex, token);
            _all.Remove(moved);
            _all.Insert(fullIndex, moved);
            Tasks.Move(oldIndex, newIndex);
        }, cancellationToken);

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

    private Task ToggleAsync(TaskRowViewModel row, CancellationToken cancellationToken) => _lifetime.RunAsync(async token =>
    {
        var previous = row.IsCompleted;
        var hadNext = row.Item.NextOccurrenceId is not null;
        await SetCompletedAsync(row, !previous, token);

        // Completing an Occurrence may have produced the next one; Ctrl+Z right after deletes it again.
        var produced = !previous && !hadNext ? row.Item.NextOccurrenceId : null;
        PushUndo(new ToggleUndo(row.Id, previous, produced));
        if (produced is not null)
        {
            await LoadAsync(token);
        }
    }, cancellationToken);

    private async Task SetCompletedAsync(TaskRowViewModel row, bool isCompleted, CancellationToken cancellationToken)
    {
        var updated = await taskService.SetCompletedAsync(row.Id, isCompleted, cancellationToken);
        row.Apply(updated);
        // Completing holds the row before it joins "Completed (N)"; in the Completed Smart View, un-completing
        // holds it before it leaves the page.
        var delay = isCompleted != IsCompletedPage ? CompletionMoveDelay() : TimeSpan.Zero;
        if (delay > TimeSpan.Zero)
        {
            // The row shows struck through and dimmed in place; the move follows without holding up the caller.
            _completionMove = _lifetime.RunAsync(token => MoveAfterAsync(row, delay, token));
        }
        else
        {
            Place(row);
        }

        OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty);
    }

    // Cancelled only by the stop, which ends it quietly.
    private async Task MoveAfterAsync(TaskRowViewModel row, TimeSpan delay, CancellationToken cancellationToken)
    {
        await Task.Delay(delay, _clock, cancellationToken);
        // Undone, deleted or reloaded meanwhile: whoever did that has placed the row already.
        var stillHeld = IsCompletedPage
            ? !row.IsCompleted && Tasks.Contains(row)
            : row.IsCompleted && !CompletedTasks.Contains(row);
        if (stillHeld && _all.Contains(row))
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
        if (IsCompletedPage)
        {
            // Completed rows are the page's main list; an un-completed one has left it.
            if (row.IsCompleted)
            {
                Tasks.Add(row);
                SortCompleted();
            }
        }
        else if (row.IsCompleted)
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
        row.IsCompleted && !IsCompletedPage ? CompletedTasks : InOverdueGroup(row) ? OverdueTasks : Tasks;

    private void NotifyCompletedChanged()
    {
        OnPropertyChanged(nameof(OverdueHeader));
        OnPropertyChanged(nameof(HasOverdueTasks));
        OnPropertyChanged(nameof(HasMoreCompleted));
        OnPropertyChanged(nameof(CompletedHeader));
        OnPropertyChanged(nameof(HasCompletedTasks));
        NotifyPageStateChanged();
    }

    // The Completed Smart View shows its Tasks in the main list and leaves "Completed (N)" empty.
    private bool IsCompletedPage => CurrentView == SmartView.Completed;

    private void SortCompleted()
    {
        var target = IsCompletedPage ? Tasks : CompletedTasks;
        var sorted = target.OrderByDescending(r => r.Item.CompletedUtc).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var from = target.IndexOf(sorted[i]);
            if (from != i)
            {
                target.Move(from, i);
            }
        }
    }
}
