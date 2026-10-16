namespace TaskDesk.Core.Tasks;

/// <summary>A built-in, read-only view of Tasks across all Lists, selected by Due and completion.</summary>
public enum SmartView
{
    Today,
    Upcoming,
    Overdue,
    All,
    Completed,
}
