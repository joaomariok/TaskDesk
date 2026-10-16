namespace TaskDesk.Core.Tasks;

/// <summary>Use cases for the Due of a Task and for the Smart Views that select by it.</summary>
public interface IDueService
{
    /// <summary>Number of completed Tasks per page of the Completed Smart View.</summary>
    const int CompletedPageSize = 100;

    /// <summary>
    /// Sets or clears the Due of a Task (local calendar date and optional local time).
    /// Returns the Task with its Tags and Subtasks loaded.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Task has this id.</exception>
    /// <exception cref="ArgumentException">A Due time without a Due date.</exception>
    Task<TaskItem> SetDueAsync(
        Guid taskId, DateOnly? dueDate, TimeOnly? dueTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the Tasks of a Smart View across all Lists, with their Tags and Subtasks loaded. Today,
    /// Upcoming and Overdue are sorted by Due; All by List then manual order; Completed newest first, one
    /// page of <see cref="CompletedPageSize"/> starting at <paramref name="skip"/> (ignored by the other
    /// views).
    /// </summary>
    Task<IReadOnlyList<TaskItem>> ListSmartViewAsync(
        SmartView view, int skip = 0, CancellationToken cancellationToken = default);
}
