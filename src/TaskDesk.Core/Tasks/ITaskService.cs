namespace TaskDesk.Core.Tasks;

/// <summary>Use cases for creating and listing Tasks.</summary>
public interface ITaskService
{
    /// <summary>Creates a Task in the Default List.</summary>
    /// <exception cref="ArgumentException">The title is empty or too long.</exception>
    Task<TaskItem> CreateTaskAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>Lists the Tasks of the Default List in their manual order.</summary>
    Task<IReadOnlyList<TaskItem>> ListDefaultListTasksAsync(CancellationToken cancellationToken = default);
}
