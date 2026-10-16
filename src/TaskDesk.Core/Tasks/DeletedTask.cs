namespace TaskDesk.Core.Tasks;

/// <summary>
/// An in-memory snapshot of a deleted Task, enough to re-insert it with the same ids: the Task itself and
/// the ids of the Tags it carried. Tickets that add Subtasks extend this snapshot with them.
/// </summary>
public sealed record DeletedTask(TaskItem Task, IReadOnlyList<Guid> TagIds);
