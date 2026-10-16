namespace TaskDesk.Core.Tasks;

/// <summary>Use cases for creating and listing Tasks.</summary>
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
}
