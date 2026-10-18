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
    private async Task AddSubtaskAsync(CancellationToken cancellationToken)
    {
        if (!CanAddSubtask())
        {
            return;
        }

        var row = DetailTask!;
        var subtask = await taskService.AddSubtaskAsync(row.Id, NewSubtaskTitle, cancellationToken);
        row.Subtasks.Add(subtask);
        row.RefreshSubtaskProgress();
        NewSubtaskTitle = "";
        if (row == DetailTask)
        {
            RebuildSubtaskItems();
        }
    }

    [RelayCommand]
    private Task DeleteSubtaskAsync(SubtaskRowViewModel? subtask, CancellationToken cancellationToken)
    {
        if (subtask is null || DetailTask is not { } row)
        {
            return Task.CompletedTask;
        }

        // A rename or tick still in flight lands before the Subtask goes.
        return EnqueueSubtaskChange(() => DeleteSubtaskNowAsync(row, subtask, cancellationToken));
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

        var newIndex = SubtaskItems.IndexOf(subtask) + offset;
        if (newIndex < 0 || newIndex >= SubtaskItems.Count)
        {
            return Task.CompletedTask;
        }

        return EnqueueSubtaskChange(() => MoveSubtaskNowAsync(row, subtask, newIndex, cancellationToken));
    }

    private async Task MoveSubtaskNowAsync(
        TaskRowViewModel row, SubtaskRowViewModel subtask, int newIndex, CancellationToken cancellationToken)
    {
        var ordered = await taskService.MoveSubtaskAsync(subtask.Id, newIndex, cancellationToken);
        row.Subtasks.Clear();
        row.Subtasks.AddRange(ordered);
        if (row == DetailTask)
        {
            RebuildSubtaskItems();
        }
    }

    /// <summary>
    /// Joins a Subtask delete or move to the chain of saves (<see cref="EnqueueSave"/>), so it runs after the saves
    /// already pending, a later save waits for it, and the stop waits for it. The change runs through the lifetime:
    /// once the view model has stopped it starts nothing, also when it was queued before the stop. The returned
    /// task completes with the change and carries its failure to the command; the chain carries on past a failure.
    /// </summary>
    private Task EnqueueSubtaskChange(Func<Task> change)
    {
        var link = Task.CompletedTask;
        EnqueueSave(previous =>
        {
            link = RunSubtaskChangeAsync(previous, change);
            return ContinuePastFailureAsync(link);
        });
        return link;
    }

    private async Task RunSubtaskChangeAsync(Task previous, Func<Task> change)
    {
        await previous; // saves of one Task never overlap

        // The change keeps the command's token, not the stop's: a change in flight at the stop lands, as a save does.
        await _lifetime.RunAsync(_ => change());
    }

    private static async Task ContinuePastFailureAsync(Task link)
    {
        try
        {
            await link;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The command that queued the change reports it; later saves still run.
        }
    }

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

    // A title edit or a tick made in the pane. Saved in order with the other pending saves.
    private void OnSubtaskEdited(SubtaskRowViewModel subtask, SubtaskEdit edit)
    {
        if (DetailTask is { } row)
        {
            EnqueueSave(previous => SaveSubtaskAsync(
                previous, row, subtask, edit, subtask.Title, subtask.IsCompleted));
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
