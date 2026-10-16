namespace TaskDesk.Core.Tasks;

/// <summary>Creates a Task in a chosen List (used by the global quick-add window).</summary>
public interface ITaskCreator
{
    /// <summary>Creates a Task at the end of the given List.</summary>
    /// <exception cref="ArgumentException">The title is empty or too long.</exception>
    /// <exception cref="KeyNotFoundException">No such List.</exception>
    Task<TaskItem> CreateTaskInListAsync(string title, Guid listId, CancellationToken cancellationToken = default);
}
