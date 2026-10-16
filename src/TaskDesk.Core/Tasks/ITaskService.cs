namespace TaskDesk.Core.Tasks;

/// <summary>Use cases for creating, listing, editing, completing and deleting Tasks.</summary>
public interface ITaskService
{
    /// <summary>Creates a Task in the Default List.</summary>
    /// <exception cref="ArgumentException">The title is empty or too long.</exception>
    Task<TaskItem> CreateTaskAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>Lists the Tasks of the Default List in their manual order.</summary>
    Task<IReadOnlyList<TaskItem>> ListDefaultListTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a Task to <paramref name="newIndex"/> within its List's manual order (zero-based, in the
    /// List's full order after the move). Persists the new position; renormalises when gaps get too small.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Task has this id.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the List.</exception>
    Task MoveTaskAsync(Guid taskId, int newIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes or un-completes a Task: completing sets <c>IsCompleted</c> and <c>CompletedUtc</c>,
    /// un-completing clears both. Setting the state it already has changes nothing.
    /// Returns the Task with its Tags loaded.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Task has this id.</exception>
    Task<TaskItem> SetCompletedAsync(Guid taskId, bool isCompleted, CancellationToken cancellationToken = default);

    /// <summary>Edits the title, notes and Priority of a Task. Returns the Task with its Tags loaded.</summary>
    /// <exception cref="KeyNotFoundException">No Task has this id.</exception>
    /// <exception cref="ArgumentException">The title or the notes are invalid.</exception>
    Task<TaskItem> UpdateTaskAsync(
        Guid taskId, string title, string? notes, Priority priority, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a Task immediately, with its Tag links, and returns a snapshot that
    /// <see cref="RestoreTaskAsync"/> can re-insert.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Task has this id.</exception>
    Task<DeletedTask> DeleteTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-inserts a deleted Task with the same ids, order and timestamps, and re-links the Tags of the
    /// snapshot that still exist. Returns the Task with its Tags loaded.
    /// </summary>
    /// <exception cref="InvalidOperationException">A Task with this id exists already.</exception>
    Task<TaskItem> RestoreTaskAsync(DeletedTask deleted, CancellationToken cancellationToken = default);
}
