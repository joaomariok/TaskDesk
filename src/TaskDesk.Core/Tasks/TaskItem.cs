namespace TaskDesk.Core.Tasks;

/// <summary>A Task. Named TaskItem in code to avoid clashing with System.Threading.Tasks.Task.</summary>
public sealed class TaskItem
{
    public Guid Id { get; set; }
    public Guid ListId { get; set; }
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public Priority Priority { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public double SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    /// <summary>Tags, loaded only by queries that include them.</summary>
    public List<TaskDesk.Core.Tags.Tag> Tags { get; set; } = [];

    /// <summary>Subtasks in their manual order, loaded only by queries that include them.</summary>
    public List<Subtask> Subtasks { get; set; } = [];
}
