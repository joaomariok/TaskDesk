namespace TaskDesk.Core.Tags;

/// <summary>Join row linking a Task to a Tag.</summary>
public sealed class TaskTag
{
    public Guid TaskId { get; set; }
    public Guid TagId { get; set; }
}
