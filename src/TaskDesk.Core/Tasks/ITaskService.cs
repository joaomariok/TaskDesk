namespace TaskDesk.Core.Tasks;

/// <summary>Use cases for creating, listing, editing, completing and deleting Tasks.</summary>
public interface ITaskService
{
    /// <summary>Creates a Task in the Default List.</summary>
    /// <exception cref="ArgumentException">The title is empty or too long.</exception>
    Task<TaskItem> CreateTaskAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>Lists the Tasks of the Default List in their manual order.</summary>
    Task<IReadOnlyList<TaskItem>> ListDefaultListTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the Tasks of one List in their manual order; none when no List has this id.</summary>
    Task<IReadOnlyList<TaskItem>> ListTasksInListAsync(Guid listId, CancellationToken cancellationToken = default);

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

    /// <summary>Adds a Subtask (1 to 500 characters) at the end of the Task's checklist.</summary>
    /// <exception cref="KeyNotFoundException">No Task has this id.</exception>
    /// <exception cref="ArgumentException">The title is empty or too long.</exception>
    Task<Subtask> AddSubtaskAsync(Guid taskId, string title, CancellationToken cancellationToken = default);

    /// <exception cref="KeyNotFoundException">No Subtask has this id.</exception>
    /// <exception cref="ArgumentException">The title is empty or too long.</exception>
    Task<Subtask> RenameSubtaskAsync(Guid subtaskId, string title, CancellationToken cancellationToken = default);

    /// <summary>Ticks or un-ticks a Subtask. Never changes the completion of the Task.</summary>
    /// <exception cref="KeyNotFoundException">No Subtask has this id.</exception>
    Task<Subtask> SetSubtaskCompletedAsync(
        Guid subtaskId, bool isCompleted, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a Subtask to <paramref name="newIndex"/> within its Task's checklist and returns the whole
    /// checklist in its new order.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Subtask has this id.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the checklist.</exception>
    Task<IReadOnlyList<Subtask>> MoveSubtaskAsync(Guid subtaskId, int newIndex, CancellationToken cancellationToken = default);

    /// <summary>Deletes a Subtask and returns it so <see cref="RestoreSubtaskAsync"/> can undo that.</summary>
    /// <exception cref="KeyNotFoundException">No Subtask has this id.</exception>
    Task<Subtask> DeleteSubtaskAsync(Guid subtaskId, CancellationToken cancellationToken = default);

    /// <summary>Re-inserts a deleted Subtask with the same id and position.</summary>
    /// <exception cref="KeyNotFoundException">Its Task does not exist any more.</exception>
    /// <exception cref="InvalidOperationException">A Subtask with this id exists already.</exception>
    Task<Subtask> RestoreSubtaskAsync(Subtask deleted, CancellationToken cancellationToken = default);
}
