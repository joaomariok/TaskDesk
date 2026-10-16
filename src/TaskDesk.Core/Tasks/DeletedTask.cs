namespace TaskDesk.Core.Tasks;

/// <summary>
/// An in-memory snapshot of a deleted Task, enough to re-insert it with the same ids: the Task itself,
/// the ids of the Tags it carried and its Subtasks.
/// </summary>
public sealed record DeletedTask(TaskItem Task, IReadOnlyList<Guid> TagIds, IReadOnlyList<Subtask> Subtasks);
