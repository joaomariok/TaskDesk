using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

// The one Task list's groups, built over the three row collections, and which Tasks show their Subtask rows.
public sealed partial class MainViewModel
{
    private TaskGroup? _overdueGroup;
    private TaskGroup? _openGroup;
    private TaskGroup? _completedGroup;
    private ObservableCollection<TaskGroup>? _groups;

    /// <summary>
    /// The groups of the one Task list, in order: Overdue, open, Completed. A group is here only while it has
    /// rows; Completed is here while there are completed Tasks, and shows them only while it is expanded.
    /// </summary>
    public ObservableCollection<TaskGroup> Groups => _groups ??= CreateGroups();

    private ObservableCollection<TaskGroup> CreateGroups()
    {
        _overdueGroup = new TaskGroup(TaskGroupKind.Overdue, OverdueTasks);
        _openGroup = new TaskGroup(TaskGroupKind.Open, Tasks);
        _completedGroup = new TaskGroup(TaskGroupKind.Completed, CompletedTasks) { IsExpanded = IsCompletedExpanded };
        ((INotifyPropertyChanged)_completedGroup).PropertyChanged += OnCompletedGroupChanged;
        var groups = new ObservableCollection<TaskGroup>();
        _groups = groups;
        OverdueTasks.CollectionChanged += (_, _) => ShowGroupWhileItHasRows(_overdueGroup, OverdueTasks.Count);
        Tasks.CollectionChanged += (_, _) => ShowGroupWhileItHasRows(_openGroup, Tasks.Count);
        CompletedTasks.CollectionChanged += (_, _) => ShowGroupWhileItHasRows(_completedGroup, CompletedTasks.Count);
        ShowGroupWhileItHasRows(_overdueGroup, OverdueTasks.Count);
        ShowGroupWhileItHasRows(_openGroup, Tasks.Count);
        ShowGroupWhileItHasRows(_completedGroup, CompletedTasks.Count);
        return groups;
    }

    // Adds the group in its place when its first row arrives, and removes it with its last row.
    private void ShowGroupWhileItHasRows(TaskGroup group, int rows)
    {
        var groups = _groups!;
        var shown = groups.Contains(group);
        if (rows > 0 && !shown)
        {
            groups.Insert(groups.Count(g => g.Kind < group.Kind), group);
        }
        else if (rows == 0 && shown)
        {
            groups.Remove(group);
        }
    }

    // The Completed heading expands and collapses the group; the view model keeps that state while the app runs.
    private void OnCompletedGroupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskGroup.IsExpanded))
        {
            IsCompletedExpanded = _completedGroup!.IsExpanded;
        }
    }

    partial void OnIsCompletedExpandedChanged(bool value)
    {
        if (_completedGroup is { } group)
        {
            group.IsExpanded = value;
        }
    }

    // ---- Open Tasks: a Task's Subtask rows under its row ----

    // The ids of the open Tasks, kept while the view model lives and never saved. Every row reads it, so the state
    // outlives the rows a page load, search or Filter change replaces.
    private readonly HashSet<Guid> _openTasks = [];

    /// <summary>
    /// Opens <paramref name="row"/>'s Task: its Subtask rows show under it. Only that Task's rows change; the
    /// selection stays. Does nothing for a Task without Subtasks.
    /// </summary>
    public void OpenTask(TaskRowViewModel row) => SetOpenByOwner(row, true);

    /// <summary>Closes <paramref name="row"/>'s Task: its Subtask rows leave the list.</summary>
    public void CloseTask(TaskRowViewModel row) => SetOpenByOwner(row, false);

    /// <summary>Opens a closed Task or closes an open one (the chevron).</summary>
    [RelayCommand]
    private void ToggleTaskOpen(TaskRowViewModel? row)
    {
        if (row is not null)
        {
            SetOpenByOwner(row, !row.IsOpen);
        }
    }

    // ---- Open by search: a Task that matches through a Subtask title shows open while the search is on ----

    // The Tasks the search opened (they were closed before it), and those the owner closed during it.
    private readonly HashSet<Guid> _searchOpened = [];
    private readonly HashSet<Guid> _searchClosed = [];

    // The owner's own choice wins over the search: a Task the owner closed stays closed until the search ends, and
    // one the owner opened is kept open after it.
    private void SetOpenByOwner(TaskRowViewModel row, bool isOpen)
    {
        _searchOpened.Remove(row.Id);
        if (SearchText.Trim().Length > 0)
        {
            if (isOpen)
            {
                _searchClosed.Remove(row.Id);
            }
            else
            {
                _searchClosed.Add(row.Id);
            }
        }

        if (isOpen)
        {
            row.SetOpen(true);
        }
        else
        {
            Close(row);
        }
    }

    // Closes a Task's Subtask rows. A selected Subtask row of it hands the selection to the Task first, while the
    // row is still listed, so the list can move a keyboard focus it holds before its container goes.
    private void Close(TaskRowViewModel row)
    {
        if (SelectedSubtaskRow?.TaskRow == row)
        {
            SelectedRow = row;
        }

        row.SetOpen(false);
    }

    // Called with the rows a load made: opens those matching through a Subtask title, and gives back the Tasks the
    // search opened earlier that no longer match.
    private void OpenTasksMatchedBySubtask(IReadOnlyList<TaskRowViewModel> rows)
    {
        var search = SearchText.Trim();
        if (search.Length == 0)
        {
            return;
        }

        var matched = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (!row.Subtasks.Exists(s => SearchFold.Contains(s.Title, search)))
            {
                continue;
            }

            matched.Add(row.Id);
            if (!row.IsOpen && !_searchClosed.Contains(row.Id))
            {
                _searchOpened.Add(row.Id);
                row.SetOpen(true);
            }
        }

        foreach (var id in _searchOpened.Where(id => !matched.Contains(id)).ToList())
        {
            _searchOpened.Remove(id);
            CloseOpenedBySearch(id);
        }
    }

    // SetOpen(false) is what removes the id and notifies the group; a Task no longer loaded only drops the id.
    private void CloseOpenedBySearch(Guid id)
    {
        var row = _all.Find(r => r.Id == id);
        if (row is null)
        {
            _openTasks.Remove(id);
            return;
        }

        Close(row);
    }

    // The search ended: every Task it opened goes back to closed, and what the owner closed during it is forgotten.
    private void EndSearchOpening()
    {
        foreach (var id in _searchOpened.ToList())
        {
            CloseOpenedBySearch(id);
        }

        _searchOpened.Clear();
        _searchClosed.Clear();
    }
}
