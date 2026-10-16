namespace TaskDesk.Core.Tasks;

/// <summary>A List: a named container that every Task belongs to exactly one of.</summary>
public sealed class TaskList
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedUtc { get; set; }
}
