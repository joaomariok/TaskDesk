namespace TaskDesk.Core.Tasks;

/// <summary>
/// The open-Task counts the sidebar shows beside the Smart Views. Each matches the number of Tasks
/// <see cref="IDueService.ListSmartViewAsync"/> lists for that view.
/// </summary>
/// <param name="Today">Open Tasks due today or earlier (the Today view includes the Overdue ones).</param>
/// <param name="Upcoming">Open Tasks due within the Upcoming window after today.</param>
/// <param name="Overdue">Open Tasks that are Overdue per <see cref="DueCalculator.IsOverdue"/>.</param>
/// <param name="All">Open Tasks in every List.</param>
public sealed record SmartViewCounts(int Today, int Upcoming, int Overdue, int All);
