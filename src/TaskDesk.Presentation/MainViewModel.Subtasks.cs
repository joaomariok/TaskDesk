using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

// The Subtasks of the selected Task in the detail pane.
public sealed partial class MainViewModel
{
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
}
