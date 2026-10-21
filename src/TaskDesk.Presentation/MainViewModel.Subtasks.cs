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

    private bool CanAddSubtask() => DetailTask is not null && TaskTitle.IsValid(NewSubtaskTitle);

    [RelayCommand(CanExecute = nameof(CanAddSubtask))]
    private Task AddSubtaskAsync(CancellationToken cancellationToken) => _lifetime.RunAsync(async token =>
    {
        if (!CanAddSubtask())
        {
            return;
        }

        var row = DetailTask!;
        var subtask = await taskService.AddSubtaskAsync(row.Id, NewSubtaskTitle, token);
        row.Subtasks.Add(subtask);
        row.RefreshSubtaskProgress();
        NewSubtaskTitle = "";
        if (row == DetailTask)
        {
            RebuildSubtaskItems();
        }
    }, cancellationToken);

    [RelayCommand]
    private Task DeleteSubtaskAsync(SubtaskRowViewModel? subtask, CancellationToken cancellationToken)
    {
        if (subtask is null || DetailTask is not { } row)
        {
            return Task.CompletedTask;
        }

        // A rename or tick still in flight lands before the Subtask goes.
        return EnqueueSubtaskChange(row, subtask, () => DeleteSubtaskNowAsync(row, subtask, cancellationToken));
    }

    private async Task DeleteSubtaskNowAsync(
        TaskRowViewModel row, SubtaskRowViewModel subtask, CancellationToken cancellationToken)
    {
        var deleted = await taskService.DeleteSubtaskAsync(subtask.Id, cancellationToken);
        row.Subtasks.RemoveAll(s => s.Id == deleted.Id);
        row.RefreshSubtaskProgress();
        if (row == DetailTask)
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
        if (row == DetailTask)
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

    private Task MoveSubtaskAsync(SubtaskRowViewModel? subtask, int offset, CancellationToken cancellationToken)
    {
        if (subtask is null || DetailTask is not { } row)
        {
            return Task.CompletedTask;
        }

        return EnqueueSubtaskChange(row, subtask, () => MoveSubtaskNowAsync(row, subtask, offset, cancellationToken));
    }

    private async Task MoveSubtaskNowAsync(
        TaskRowViewModel row, SubtaskRowViewModel subtask, int offset, CancellationToken cancellationToken)
    {
        // Read the position on its turn: a change queued ahead of this one may have moved or deleted Subtasks.
        var newIndex = row.Subtasks.FindIndex(s => s.Id == subtask.Id) + offset;
        if (newIndex < 0 || newIndex >= row.Subtasks.Count)
        {
            return;
        }

        var ordered = await taskService.MoveSubtaskAsync(subtask.Id, newIndex, cancellationToken);
        row.Subtasks.Clear();
        row.Subtasks.AddRange(ordered);
        row.RefreshSubtaskProgress();
        if (row == DetailTask)
        {
            RebuildSubtaskItems();
        }
    }

    /// <summary>
    /// Queues a delete or move of <paramref name="subtask"/> (<see cref="DetailSaveQueue.EnqueueSubtaskChange"/>): it
    /// runs after the saves already queued, a later save waits for it, and the stop waits for it. On its turn it does
    /// nothing when a change queued ahead of it deleted the Subtask. The returned task completes with the change and
    /// carries its failure to the command.
    /// </summary>
    private Task EnqueueSubtaskChange(TaskRowViewModel row, SubtaskRowViewModel subtask, Func<Task> change)
        => Saves.EnqueueSubtaskChange(() => row.Subtasks.Exists(s => s.Id == subtask.Id)
            ? change()
            : Task.CompletedTask); // a change queued ahead of this one deleted it already

    private void RebuildSubtaskItems()
    {
        SubtaskItems.Clear();
        if (DetailTask is { } row)
        {
            foreach (var subtask in row.Subtasks)
            {
                SubtaskItems.Add(new SubtaskRowViewModel(subtask, OnSubtaskEdited));
            }
        }
    }

    // A title edit or a tick made in the pane. Saved in order with the other pending saves; once the view model has
    // stopped nothing saves it, so the edited field goes back to its saved value. Only that field: the other may
    // show a save still in flight.
    private void OnSubtaskEdited(SubtaskRowViewModel subtask, SubtaskEdit edit)
    {
        if (DetailTask is not { } row)
        {
            return;
        }

        var (title, isCompleted) = (subtask.Title, subtask.IsCompleted);
        var saving = Saves.Enqueue(() => StoreSubtaskAsync(row, subtask, edit, title, isCompleted));
        if (saving is null && row.Subtasks.Find(s => s.Id == subtask.Id) is { } saved)
        {
            subtask.Apply(new Subtask
            {
                Id = saved.Id,
                TaskId = saved.TaskId,
                Title = edit == SubtaskEdit.Title ? saved.Title : subtask.Title,
                IsCompleted = edit == SubtaskEdit.Completed ? saved.IsCompleted : subtask.IsCompleted,
                SortOrder = saved.SortOrder,
            });
        }
    }

    private async Task<bool> StoreSubtaskAsync(
        TaskRowViewModel row, SubtaskRowViewModel subtask, SubtaskEdit edit, string title, bool isCompleted)
    {
        var known = row.Subtasks.FindIndex(s => s.Id == subtask.Id);
        if (known < 0)
        {
            return false; // a change queued ahead of this one deleted it
        }

        Subtask updated;
        if (edit == SubtaskEdit.Title)
        {
            if (!TaskTitle.IsValid(title))
            {
                subtask.Apply(row.Subtasks[known]); // keep the last valid saved title
                return false;
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

        return true;
    }
}
