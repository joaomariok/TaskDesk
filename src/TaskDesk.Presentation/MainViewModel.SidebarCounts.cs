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
    /// Recounts the open Tasks of Today, Upcoming, Overdue and All with count queries (no Task is loaded).
    /// Does nothing when no Due service was supplied, or once the view model has stopped (<see cref="StopAsync"/>).
    /// </summary>
    public Task RefreshSmartViewCountsAsync(CancellationToken cancellationToken = default) =>
        dueService is null ? Task.CompletedTask : _lifetime.RunAsync(RefreshCountsAsync, cancellationToken);

    private async Task RefreshCountsAsync(CancellationToken cancellationToken)
    {
        var counts = await dueService!.CountSmartViewsAsync(cancellationToken);
        TodayCount = counts.Today;
        UpcomingCount = counts.Upcoming;
        OverdueCount = counts.Overdue;
        AllCount = counts.All;
    }
}
