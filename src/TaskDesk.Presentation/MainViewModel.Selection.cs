using System.ComponentModel;

namespace TaskDesk.Presentation;

// The one selection of the Task list: a Task row, or one of its Subtask rows, which stands for its Task.
public sealed partial class MainViewModel
{
    private SubtaskListRowViewModel? _selectedSubtaskRow;

    /// <summary>
    /// The Subtask row selected in the list, or null while a Task row (or nothing) is selected. While it is set,
    /// <see cref="SelectedTask"/> is its Task: F2, Enter and a docked pane act on the Task, and Delete and Space do
    /// nothing.
    /// </summary>
    public SubtaskListRowViewModel? SelectedSubtaskRow => _selectedSubtaskRow;

    /// <summary>
    /// The row selected in the list: the selected Subtask row, else the selected Task's row. Setting a Subtask row
    /// selects its Task too; setting a Task row (or null) leaves any Subtask row.
    /// </summary>
    public ITaskListRow? SelectedRow
    {
        get => (ITaskListRow?)_selectedSubtaskRow ?? SelectedTask;
        set
        {
            // The Subtask row is set before its Task, so every change notice already reads the final selection.
            var subtask = value as SubtaskListRowViewModel;
            var changed = !ReferenceEquals(_selectedSubtaskRow, subtask);
            _selectedSubtaskRow = subtask;
            SelectedTask = value?.TaskRow;
            if (changed)
            {
                OnPropertyChanged(nameof(SelectedSubtaskRow));
                OnPropertyChanged(nameof(SelectedRow));
            }
        }
    }

    // A Task selected other than the selected Subtask row's leaves that row. The selected Task's row is watched, so
    // its Subtask row going (the Task closed, the Subtask deleted) selects the Task.
    partial void OnSelectedTaskChanged(TaskRowViewModel? oldValue, TaskRowViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSelectedTaskRowChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSelectedTaskRowChanged;
        }

        if (_selectedSubtaskRow is { } subtask && subtask.TaskRow != newValue)
        {
            _selectedSubtaskRow = null;
            OnPropertyChanged(nameof(SelectedSubtaskRow));
        }

        OnPropertyChanged(nameof(SelectedRow));
    }

    private void OnSelectedTaskRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskRowViewModel.ShownSubtaskRows) && _selectedSubtaskRow is { } subtask
            && sender is TaskRowViewModel row && subtask.TaskRow == row && !row.ShownSubtaskRows.Contains(subtask))
        {
            SelectedRow = row;
        }
    }

    // The selected Subtask row's id across a reload, which replaces every row.
    private Guid? _reselectSubtaskId;

    private void RememberSubtaskSelection() => _reselectSubtaskId = _selectedSubtaskRow?.Id;

    private void ReselectSubtaskRow()
    {
        if (SelectedTask?.ShownSubtaskRows.FirstOrDefault(s => s.Id == _reselectSubtaskId) is { } subtask)
        {
            SelectedRow = subtask;
        }
    }
}
