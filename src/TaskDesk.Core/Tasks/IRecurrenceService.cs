namespace TaskDesk.Core.Tasks;

/// <summary>
/// What a Skip did: the snapshot of the deleted Occurrence and the next Occurrence it produced (null when
/// the Series had ended or the Occurrence had already produced its next one).
/// </summary>
public sealed record SkippedOccurrence(DeletedTask Deleted, Guid? ProducedOccurrenceId);

/// <summary>
/// Use cases for recurring Tasks (ADR 0001, SPEC 5). Completing an Occurrence through
/// <see cref="ITaskService.SetCompletedAsync"/> produces its next Occurrence; "Stop repeating" is a plain
/// <see cref="ITaskService.DeleteTaskAsync"/> undone by <see cref="ITaskService.RestoreTaskAsync"/>.
/// </summary>
public interface IRecurrenceService
{
    /// <summary>
    /// Sets or clears (null) the Recurrence of a Task. Returns the Task with its Tags and Subtasks loaded.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Task has this id.</exception>
    /// <exception cref="ArgumentException">The Recurrence is invalid, or the Task has no Due date.</exception>
    Task<TaskItem> SetRecurrenceAsync(Guid taskId, Recurrence? recurrence, CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes a completion right after it was made (Ctrl+Z): un-completes the Task and, when that
    /// completion produced <paramref name="producedOccurrenceId"/>, deletes that Occurrence and clears
    /// NextOccurrenceId, in one transaction. A later manual un-complete uses
    /// <see cref="ITaskService.SetCompletedAsync"/> instead and keeps the next Occurrence.
    /// Returns the Task with its Tags and Subtasks loaded.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Task has this id.</exception>
    Task<TaskItem> UndoCompletionAsync(
        Guid taskId, Guid? producedOccurrenceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Delete this occurrence": produces the next Occurrence as a completion would (at most once, with
    /// today's local date as the completion date), then deletes this Occurrence, in one transaction.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Task has this id.</exception>
    /// <exception cref="InvalidOperationException">The Task has no Recurrence.</exception>
    Task<SkippedOccurrence> SkipOccurrenceAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes a Skip: re-inserts the Occurrence with the same ids and deletes the Occurrence the Skip
    /// produced, in one transaction. Returns the restored Task with its Tags loaded.
    /// </summary>
    /// <exception cref="InvalidOperationException">A Task with this id exists already.</exception>
    Task<TaskItem> UndoSkipAsync(SkippedOccurrence skipped, CancellationToken cancellationToken = default);
}
