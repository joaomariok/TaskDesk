using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

// Deleting Tasks, the Undo bar, and Ctrl+Z for deletes and completion toggles.
public sealed partial class MainViewModel
{
    /// <summary>Deletes a Task immediately and offers Undo for <see cref="UndoBarDuration"/>.</summary>
    [RelayCommand]
    private Task DeleteAsync(TaskRowViewModel? row, CancellationToken cancellationToken)
        => row is null ? Task.CompletedTask : DeleteRowAsync(row, cancellationToken);

    [RelayCommand]
    private Task DeleteSelectedAsync(CancellationToken cancellationToken)
        => SelectedTask is null ? Task.CompletedTask : DeleteRowAsync(SelectedTask, cancellationToken);

    private async Task DeleteRowAsync(TaskRowViewModel row, CancellationToken cancellationToken)
    {
        // Deleting an Occurrence asks whether to Skip it or stop repeating (SPEC 5.2).
        var choice = row.Item.Recurrence is null
            ? OccurrenceDeleteChoice.StopRepeating
            : OccurrenceDialogs.AskDeleteOccurrence(row.Title);
        if (choice == OccurrenceDeleteChoice.Cancel)
        {
            return;
        }

        _debounce?.Cancel();
        _debounce = null;
        if (choice == OccurrenceDeleteChoice.DeleteThisOccurrence)
        {
            var skipped = await Recurrences.SkipOccurrenceAsync(row.Id, cancellationToken);
            RemoveRow(row);
            PushUndo(new SkipUndo(skipped));
            ShowUndoBar("Task deleted");
            if (skipped.ProducedOccurrenceId is not null)
            {
                await LoadAsync(cancellationToken);
            }

            return;
        }

        var snapshot = await taskService.DeleteTaskAsync(row.Id, cancellationToken);
        RemoveRow(row);
        PushUndo(new DeleteUndo(snapshot));
        ShowUndoBar("Task deleted");
    }

    private void RemoveRow(TaskRowViewModel row)
    {
        if (SelectedTask == row)
        {
            SelectedTask = null;
        }

        _all.Remove(row);
        Tasks.Remove(row);
        OverdueTasks.Remove(row);
        CompletedTasks.Remove(row);
        NotifyCompletedChanged();
        OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty);
    }

    [ObservableProperty]
    private bool _isUndoBarVisible;

    /// <summary>Undoes the last delete or completion toggle (Ctrl+Z and the Undo bar).</summary>
    [RelayCommand]
    private async Task UndoAsync(CancellationToken cancellationToken)
    {
        var entry = _lastUndo;
        if (entry is null)
        {
            return;
        }

        _lastUndo = null;
        HideUndoBar();
        switch (entry)
        {
            case ToggleUndo { ProducedOccurrenceId: { } produced } toggle:
                await Recurrences.UndoCompletionAsync(toggle.TaskId, produced, cancellationToken);
                await LoadAsync(cancellationToken);
                OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case SkipUndo skip:
                await Recurrences.UndoSkipAsync(skip.Skipped, cancellationToken);
                await LoadAsync(cancellationToken);
                OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case ToggleUndo toggle when _all.Find(r => r.Id == toggle.TaskId) is { } row:
                await SetCompletedAsync(row, toggle.PreviousState, cancellationToken);
                break;
            case SubtaskDeleteUndo subtask:
                await RestoreSubtaskAsync(subtask.Snapshot, cancellationToken);
                break;
            case DeleteUndo delete:
                var restored = await taskService.RestoreTaskAsync(delete.Snapshot, cancellationToken);
                var restoredRow = CreateRow(restored);
                var at = _all.FindIndex(r => r.Item.SortOrder > restored.SortOrder);
                _all.Insert(at < 0 ? _all.Count : at, restoredRow);
                Place(restoredRow);
                OpenTaskCountsChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void PushUndo(UndoEntry entry)
    {
        _lastUndo = entry;
        if (entry is not (DeleteUndo or SubtaskDeleteUndo or SkipUndo))
        {
            HideUndoBar();
        }
    }

    /// <summary>What the Undo bar says before its Undo button.</summary>
    [ObservableProperty]
    private string _undoBarText = "Task deleted";

    private void ShowUndoBar(string text)
    {
        UndoBarText = text;
        _barTimer?.Cancel();
        _barTimer = new CancellationTokenSource();
        IsUndoBarVisible = true;
        UndoBarExpiry = ExpireUndoBarAsync(_barTimer.Token);
    }

    private void HideUndoBar()
    {
        _barTimer?.Cancel();
        _barTimer = null;
        IsUndoBarVisible = false;
    }

    /// <summary>Completes when the current Undo bar has timed out or been dismissed.</summary>
    public Task UndoBarExpiry { get; private set; } = Task.CompletedTask;

    private async Task ExpireUndoBarAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(UndoBarDuration, _clock, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // The bar goes away; Ctrl+Z can still undo the delete until another action replaces it.
        IsUndoBarVisible = false;
    }

    private abstract record UndoEntry;

    // ProducedOccurrenceId: the next Occurrence this completion produced, deleted again by the undo.
    private sealed record ToggleUndo(Guid TaskId, bool PreviousState, Guid? ProducedOccurrenceId) : UndoEntry;

    private sealed record SkipUndo(SkippedOccurrence Skipped) : UndoEntry;

    private sealed record DeleteUndo(DeletedTask Snapshot) : UndoEntry;

    private sealed record SubtaskDeleteUndo(Subtask Snapshot) : UndoEntry;
}
