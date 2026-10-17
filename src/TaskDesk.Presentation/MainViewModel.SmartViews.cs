using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

// Smart Views (Ctrl+1 to Ctrl+5), Completed paging, and the refresh as time passes (Today, Overdue).
public sealed partial class MainViewModel
{
    /// <summary>
    /// Shows a Smart View across all Lists, or the Default List again when null. Smart Views are not
    /// Tag-filtered, and they turn reordering off.
    /// </summary>
    public async Task ShowSmartViewAsync(SmartView? view, CancellationToken cancellationToken = default)
    {
        TagFilter = null;
        CurrentView = view;
        await LoadAsync(cancellationToken);
    }

    /// <summary>Switches to a Smart View (Ctrl+1 to Ctrl+5).</summary>
    [RelayCommand]
    private Task SelectSmartViewAsync(SmartView view, CancellationToken cancellationToken)
        => ShowSmartViewAsync(view, cancellationToken);

    /// <summary>True when the Completed view may have more Tasks than the pages loaded so far.</summary>
    public bool HasMoreCompleted => CurrentView == SmartView.Completed && _completedHasMore;

    /// <summary>Loads the next page of 100 in the Completed view.</summary>
    [RelayCommand]
    private async Task LoadMoreCompletedAsync(CancellationToken cancellationToken)
    {
        if (CurrentView != SmartView.Completed)
        {
            return;
        }

        var page = IsNarrowed
            ? await Search.SearchAsync(BuildSearchQuery(_completedFetched), cancellationToken)
            : await Due.ListSmartViewAsync(SmartView.Completed, _completedFetched, cancellationToken);
        _completedFetched += page.Count;
        _completedHasMore = page.Count == IDueService.CompletedPageSize;
        foreach (var task in page)
        {
            var row = CreateRow(task);
            _all.Add(row);
            GroupOf(row).Add(row);
        }

        SortCompleted();
        NotifyCompletedChanged();
    }

    private ITimer? _dueRefreshTimer;
    private SynchronizationContext? _uiContext;
    private DateOnly _loadedToday;
    private Task _dueRefresh = Task.CompletedTask;

    /// <summary>Completes when the refresh started by the last local midnight or passed Due time has finished.</summary>
    public Task DueRefresh => _dueRefresh;

    // Arms a one-shot timer for the next instant the Due state of the loaded Tasks can change: local midnight
    // or the next timed Due of today (DueCalculator.NextChange). Every refresh arms the next one.
    private void ScheduleDueRefresh()
    {
        _uiContext ??= SynchronizationContext.Current; // the UI thread in the app; none in tests
        var now = _clock.GetUtcNow();
        var due = DueCalculator.NextChange(_all.Select(r => r.Item), now, _clock.LocalTimeZone) - now;
        _dueRefreshTimer?.Dispose();
        _dueRefreshTimer = _clock.CreateTimer(
            _ => OnDueRefreshTimer(), null, due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
    }

    private void OnDueRefreshTimer()
    {
        var done = new TaskCompletionSource();
        _dueRefresh = done.Task;
#pragma warning disable VSTHRD101 // Async void is intended: the body catches everything and always completes the TCS
        SendOrPostCallback run = async _ =>
        {
            try
            {
                await RefreshDueStateAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ErrorMessage = "Could not refresh the view: " + ex.Message;
            }
            finally
            {
                done.TrySetResult();
            }
        };
#pragma warning restore VSTHRD101

        if (_uiContext is { } context)
        {
            context.Post(run, null);
        }
        else
        {
            run(null);
        }
    }

    private async Task RefreshDueStateAsync()
    {
        var today = DueCalculator.LocalToday(_clock.GetUtcNow(), _clock.LocalTimeZone);
        // Which Tasks a Smart View holds depends on the date (and, for Overdue, on the time): reload it.
        if (CurrentView == SmartView.Overdue
            || (today != _loadedToday && CurrentView is SmartView.Today or SmartView.Upcoming))
        {
            await LoadAsync();
            return;
        }

        foreach (var row in _all)
        {
            var wasOverdueGroup = OverdueTasks.Contains(row);
            row.RefreshDue();
            if (!row.IsCompleted && InOverdueGroup(row) != wasOverdueGroup)
            {
                Place(row);
            }
        }

        _loadedToday = today;
        ScheduleDueRefresh();
    }
}
