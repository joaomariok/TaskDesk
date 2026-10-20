using System.Collections.ObjectModel;
using System.ComponentModel;

namespace TaskDesk.Presentation;

// The one Task list's groups, built over the three row collections.
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
}
