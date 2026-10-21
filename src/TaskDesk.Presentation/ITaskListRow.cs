namespace TaskDesk.Presentation;

/// <summary>
/// One row of the one Task list: a Task's row (<see cref="TaskRowViewModel"/>) or, under an open Task, one of its
/// Subtask rows (<see cref="SubtaskListRowViewModel"/>). Every row belongs to one Task.
/// </summary>
public interface ITaskListRow
{
    /// <summary>The title the row shows: the Task's or the Subtask's.</summary>
    string Title { get; }

    /// <summary>The Task the row belongs to: the Task row itself, or a Subtask row's Task.</summary>
    TaskRowViewModel TaskRow { get; }
}
