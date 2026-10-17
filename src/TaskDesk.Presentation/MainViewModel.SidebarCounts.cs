using CommunityToolkit.Mvvm.ComponentModel;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

// The open-Task counts the sidebar shows beside the Smart Views (Overdue as a red badge).
public sealed partial class MainViewModel
{
    /// <summary>Open Tasks due today (the Today Smart View, Overdue Tasks included).</summary>
    [ObservableProperty]
    private int _todayCount;

    /// <summary>Open Tasks due within the Upcoming window.</summary>
    [ObservableProperty]
    private int _upcomingCount;

    /// <summary>Overdue Tasks; the sidebar shows it as a badge.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverdueCount))]
    private int _overdueCount;

    /// <summary>Open Tasks in every List.</summary>
    [ObservableProperty]
    private int _allCount;

    /// <summary>True when the Overdue badge has something to show.</summary>
    public bool HasOverdueCount => OverdueCount > 0;

    /// <summary>
    /// Recounts the open Tasks of Today, Upcoming, Overdue and All from the Smart View queries. Does
    /// nothing when no Due service was supplied.
    /// </summary>
    public async Task RefreshSmartViewCountsAsync(CancellationToken cancellationToken = default)
    {
        if (dueService is null)
        {
            return;
        }

        var today = await dueService.ListSmartViewAsync(SmartView.Today, 0, cancellationToken);
        var upcoming = await dueService.ListSmartViewAsync(SmartView.Upcoming, 0, cancellationToken);
        var overdue = await dueService.ListSmartViewAsync(SmartView.Overdue, 0, cancellationToken);
        var all = await dueService.ListSmartViewAsync(SmartView.All, 0, cancellationToken);
        TodayCount = today.Count;
        UpcomingCount = upcoming.Count;
        OverdueCount = overdue.Count;
        AllCount = all.Count;
    }
}
