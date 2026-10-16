namespace TaskDesk.Core.Tasks;

/// <summary>
/// A one-level checklist step inside a Task. Ticking Subtasks and completing their Task are independent.
/// </summary>
public sealed class Subtask
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public double SortOrder { get; set; }
}
