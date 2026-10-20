using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace TaskDesk.Presentation;

/// <summary>The kind of a group in the one Task list, in the order the groups show.</summary>
public enum TaskGroupKind
{
    /// <summary>The Overdue Tasks at the top of the Today view, under "Overdue (N)".</summary>
    Overdue,

    /// <summary>The open Tasks, with no heading (in the Completed Smart View, its completed Tasks).</summary>
    Open,

    /// <summary>The completed Tasks under the collapsible "Completed (N)" heading.</summary>
    Completed,
}

/// <summary>
/// One group of the one Task list: itself the collection of the rows it shows, so a grouped list needs no item
/// path. It mirrors one of the main view model's row collections. The Completed group shows its rows only while
/// it is expanded; its heading still counts every completed Task.
/// </summary>
public sealed class TaskGroup : ObservableCollection<TaskRowViewModel>
{
    private readonly ObservableCollection<TaskRowViewModel> _source;
    private bool _isExpanded;

    internal TaskGroup(TaskGroupKind kind, ObservableCollection<TaskRowViewModel> source)
    {
        Kind = kind;
        _source = source;
        _isExpanded = kind != TaskGroupKind.Completed;
        Mirror();
        source.CollectionChanged += OnSourceChanged;
    }

    public TaskGroupKind Kind { get; }

    /// <summary>"Overdue (N)" or "Completed (N)" with N every row of the group; empty for the open group.</summary>
    public string Heading => Kind switch
    {
        TaskGroupKind.Overdue => $"Overdue ({_source.Count})",
        TaskGroupKind.Completed => $"Completed ({_source.Count})",
        _ => string.Empty,
    };

    /// <summary>Whether the group shows its rows. Only the Completed group collapses; it starts collapsed.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value || Kind != TaskGroupKind.Completed)
            {
                return;
            }

            _isExpanded = value;
            ShowOrHideRows();
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    /// <summary>
    /// The group's accessible name: screen readers announce each group of the list by it. The open group is
    /// "Tasks", as the list itself is.
    /// </summary>
    public override string ToString() => Kind switch
    {
        TaskGroupKind.Overdue => "Overdue tasks",
        TaskGroupKind.Completed => "Completed tasks",
        _ => "Tasks",
    };

    // Changes are mirrored one by one, so the list keeps its containers and its selection where it can.
    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isExpanded)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    for (var i = 0; i < e.NewItems!.Count; i++)
                    {
                        Insert(e.NewStartingIndex + i, (TaskRowViewModel)e.NewItems[i]!);
                    }

                    break;
                case NotifyCollectionChangedAction.Remove:
                    for (var i = 0; i < e.OldItems!.Count; i++)
                    {
                        RemoveAt(e.OldStartingIndex);
                    }

                    break;
                case NotifyCollectionChangedAction.Move:
                    Move(e.OldStartingIndex, e.NewStartingIndex);
                    break;
                default:
                    Mirror();
                    break;
            }
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Heading)));
    }

    // Adds or removes the rows one by one: a reset would make the list rebuild the group, heading and all, and
    // the heading would lose the keyboard focus that just toggled it.
    private void ShowOrHideRows()
    {
        if (_isExpanded)
        {
            for (var i = Count; i < _source.Count; i++)
            {
                Add(_source[i]);
            }
        }
        else
        {
            while (Count > 0)
            {
                RemoveAt(Count - 1);
            }
        }
    }

    // Shows every source row while expanded, none while collapsed.
    private void Mirror()
    {
        Clear();
        if (_isExpanded)
        {
            foreach (var row in _source)
            {
                Add(row);
            }
        }
    }
}
