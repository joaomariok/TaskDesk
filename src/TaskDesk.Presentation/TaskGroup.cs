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
/// path. It mirrors one of the main view model's Task row collections, each Task row followed by its Subtask rows
/// while the Task is open. The Completed group shows its rows only while it is expanded; its heading still counts
/// every completed Task, and only Tasks.
/// </summary>
public sealed class TaskGroup : ObservableCollection<ITaskListRow>
{
    private readonly ObservableCollection<TaskRowViewModel> _source;
    private readonly HashSet<TaskRowViewModel> _watched = [];
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

    // Changes are mirrored one by one, so the list keeps its containers and its selection where it can. A Task row
    // moves, comes and goes with its Subtask rows: its block.
    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                for (var i = 0; i < e.NewItems!.Count; i++)
                {
                    var row = (TaskRowViewModel)e.NewItems[i]!;
                    Watch(row);
                    if (_isExpanded)
                    {
                        InsertBlock(BlockStartOf(e.NewStartingIndex + i), row);
                    }
                }

                break;
            case NotifyCollectionChangedAction.Remove:
                foreach (TaskRowViewModel row in e.OldItems!)
                {
                    Unwatch(row);
                    RemoveBlock(row);
                }

                break;
            case NotifyCollectionChangedAction.Move:
                var moved = (TaskRowViewModel)e.OldItems![0]!;
                if (_isExpanded)
                {
                    RemoveBlock(moved);
                    InsertBlock(BlockStartOf(e.NewStartingIndex), moved);
                }

                break;
            default:
                Mirror();
                break;
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Heading)));
    }

    // Where the source row at sourceIndex starts its block: where the next shown source row starts, or the end.
    private int BlockStartOf(int sourceIndex)
    {
        for (var i = sourceIndex + 1; i < _source.Count; i++)
        {
            var at = IndexOf(_source[i]);
            if (at >= 0)
            {
                return at;
            }
        }

        return Count;
    }

    private void InsertBlock(int index, TaskRowViewModel row)
    {
        Insert(index, row);
        foreach (var subtask in row.ShownSubtaskRows)
        {
            Insert(++index, subtask);
        }
    }

    private void RemoveBlock(TaskRowViewModel row)
    {
        var at = IndexOf(row);
        if (at < 0)
        {
            return;
        }

        // The Subtask rows go first, from the last, so the Task row is the last to leave.
        for (var end = BlockEnd(row, at); end > at + 1; end--)
        {
            RemoveAt(end - 1);
        }

        RemoveAt(at);
    }

    // The index just past the Subtask rows that follow the Task row at taskIndex.
    private int BlockEnd(TaskRowViewModel row, int taskIndex)
    {
        var end = taskIndex + 1;
        while (end < Count && this[end] is SubtaskListRowViewModel subtask && subtask.TaskRow == row)
        {
            end++;
        }

        return end;
    }

    // Opening, closing or a Subtask change touches only that Task's Subtask rows: a row that stays in place is kept,
    // and one whose place changed is removed and inserted again (the list then selects it again if it was selected).
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TaskRowViewModel.ShownSubtaskRows) || !_isExpanded
            || sender is not TaskRowViewModel row)
        {
            return;
        }

        var at = IndexOf(row);
        if (at < 0)
        {
            return;
        }

        var wanted = row.ShownSubtaskRows;
        for (var i = BlockEnd(row, at) - 1; i > at; i--)
        {
            if (!wanted.Contains(this[i]))
            {
                RemoveAt(i);
            }
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var index = at + 1 + i;
            var from = IndexOf(wanted[i]);
            if (from == index)
            {
                continue;
            }

            // A row already in the block sits further down; it is taken out and put back rather than moved, as a
            // grouped list resets on a move and every row of the list blanks and is realised again.
            if (from >= 0)
            {
                RemoveAt(from);
            }

            Insert(index, wanted[i]);
        }
    }

    // A Task row is watched while it is in the source, shown or not, so a collapsed group shows it right on expanding.
    private void Watch(TaskRowViewModel row)
    {
        if (_watched.Add(row))
        {
            row.PropertyChanged += OnRowChanged;
        }
    }

    private void Unwatch(TaskRowViewModel row)
    {
        if (_watched.Remove(row))
        {
            row.PropertyChanged -= OnRowChanged;
        }
    }

    // Adds or removes the rows one by one: a reset would make the list rebuild the group, heading and all, and
    // the heading would lose the keyboard focus that just toggled it.
    private void ShowOrHideRows()
    {
        if (_isExpanded)
        {
            foreach (var row in _source)
            {
                InsertBlock(Count, row);
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

    // Shows every source row's block while expanded, none while collapsed, and watches exactly the source rows.
    private void Mirror()
    {
        foreach (var row in _watched.ToList())
        {
            Unwatch(row);
        }

        Clear();
        foreach (var row in _source)
        {
            Watch(row);
            if (_isExpanded)
            {
                InsertBlock(Count, row);
            }
        }
    }
}
