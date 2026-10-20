using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

/// <summary>A Tag chip of the filter bar.</summary>
public sealed record FilterTagChip(Tag Tag, bool IsSelected);

// Search (Ctrl+F) and filter chips. They narrow the current view; clearing them restores it.
public sealed partial class MainViewModel
{
    /// <summary>How long the search waits for typing to pause before it reloads the page.</summary>
    public static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);

    // Searches, loads, count refreshes and timers stop with the view model (see StopAsync).
    private readonly ViewModelLifetime _lifetime = new();

    private ITimer? _searchTimer;

    // Each queued search takes a version; a tick that was already posted runs only if no later queue or drop
    // came after it.
    private int _searchVersion;

    /// <summary>True while a search waits for typing to pause before it reloads the page.</summary>
    public bool IsSearchPending => _searchTimer is not null;

    /// <summary>
    /// Saves a pending detail edit, then stops the view model's deferred work: from now on nothing starts, and work
    /// still running is cancelled and ends quietly. That is the search (waiting or loading), <see cref="LoadAsync"/>,
    /// the Smart View count refresh, the due refresh timer (disposed, never armed again), the detail save debounce,
    /// the Undo bar timer, the completion hold, and every command that reaches a service (a dialog it shows stays
    /// outside, so the stop never waits on the owner). Idempotent; the save starts and the stop happens before this
    /// returns, and the task completes once the save has landed and the cancelled work has ended, however it ended.
    /// A failed save shows in <see cref="ErrorMessage"/>, as any detail save does.
    /// </summary>
    public Task StopAsync()
    {
        // First: once the lifetime has stopped, an edit saves nothing.
        var saving = FlushPendingSaveAsync();
        var stopping = _lifetime.StopAsync();
        DropQueuedSearch();
        StopDueRefresh();
        return Task.WhenAll(saving, stopping);
    }

    /// <summary>The search text, matched against titles, notes and Subtask titles of the current view.</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>The lowest Priority shown, or null for any.</summary>
    [ObservableProperty]
    private Priority? _minPriorityFilter;

    /// <summary>Only show Tasks that have a Due date.</summary>
    [ObservableProperty]
    private bool _hasDueFilter;

    /// <summary>Only show Tasks that have a Reminder.</summary>
    [ObservableProperty]
    private bool _hasReminderFilter;

    /// <summary>The Tags a Task must all carry to be shown.</summary>
    public ObservableCollection<Tag> FilterTags { get; } = [];

    /// <summary>One chip per Tag, marking those in <see cref="FilterTags"/>.</summary>
    public ObservableCollection<FilterTagChip> FilterTagChips { get; } = [];

    /// <summary>True while a search text or any filter chip narrows the view.</summary>
    public bool IsNarrowed => !BuildSearchQuery(0).IsEmpty;

    /// <summary>True while any Filter is on. The search text does not count: search is not a Filter.</summary>
    public bool HasFilter =>
        MinPriorityFilter is not null || HasDueFilter || HasReminderFilter || FilterTags.Count > 0;

    /// <summary>The owner's choice to show the Filter row. Not saved: it starts closed at every launch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterBarShown))]
    private bool _isFilterBarOpen;

    /// <summary>True while the Filter row is shown: the owner opened it, or a Filter is on and must stay visible.</summary>
    public bool IsFilterBarShown => IsFilterBarOpen || HasFilter;

    /// <summary>Opens or closes the Filter row. While a Filter is on, the row stays shown either way.</summary>
    [RelayCommand]
    private void ToggleFilterBar()
    {
        if (HasFilter)
        {
            // The row cannot be hidden now. Report it so a pressed-looking toggle is pushed back.
            OnPropertyChanged(nameof(IsFilterBarShown));
            return;
        }

        IsFilterBarOpen = !IsFilterBarOpen;
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsNarrowed));
        UpdateReorderEnabled();
        NotifyPageStateChanged();
        QueueSearch();
    }

    // Each keystroke restarts the wait, so the search reloads once typing pauses.
    private void QueueSearch()
    {
        if (_lifetime.IsStopped)
        {
            return;
        }

        _uiContext ??= SynchronizationContext.Current; // the UI thread in the app; none in tests
        _searchTimer?.Dispose();
        var version = ++_searchVersion;
        _searchTimer = _clock.CreateTimer(_ => OnSearchTimer(version), null, SearchDelay, Timeout.InfiniteTimeSpan);
    }

    private void OnSearchTimer(int version) => OnUiThread(_ => RunQueuedSearch(version));

    private void RunQueuedSearch(int version)
    {
        if (version != _searchVersion || _lifetime.IsStopped)
        {
            return; // dropped, restarted by more typing, or the view model has stopped
        }

        DropQueuedSearch();
        ApplySearchCommand.Execute(null);
    }

    // The view model's own clears of the search text load straight away, so the search they queued is dropped.
    private void ClearSearchText()
    {
        SearchText = "";
        DropQueuedSearch();
    }

    private void DropQueuedSearch()
    {
        _searchVersion++;
        _searchTimer?.Dispose();
        _searchTimer = null;
    }

    partial void OnMinPriorityFilterChanged(Priority? value) => OnFilterStateChanged();

    partial void OnHasDueFilterChanged(bool value) => OnFilterStateChanged();

    partial void OnHasReminderFilterChanged(bool value) => OnFilterStateChanged();

    // The one path for every change to a Filter, including FilterTags, which no generated property
    // setter reports. Callers must go through it so nothing is left stale.
    private void OnFilterStateChanged()
    {
        OnPropertyChanged(nameof(IsNarrowed));
        OnPropertyChanged(nameof(HasFilter));
        OnPropertyChanged(nameof(IsFilterBarShown));
        UpdateReorderEnabled();
        NotifyPageStateChanged();
    }

    private TaskSearchQuery BuildSearchQuery(int skip) => new(
        SearchText,
        MinPriorityFilter,
        [.. FilterTags.Select(t => t.Id)],
        HasDueFilter,
        CurrentView,
        TagFilter?.Id,
        skip,
        HasReminderFilter,
        CurrentListId);

    // Re-selects the chips of Tags that still exist, dropping those that were deleted.
    private void PruneFilterTags()
    {
        var removed = false;
        for (var i = FilterTags.Count - 1; i >= 0; i--)
        {
            var current = Tags.FirstOrDefault(t => t.Id == FilterTags[i].Id);
            if (current is null)
            {
                FilterTags.RemoveAt(i);
                removed = true;
            }
            else
            {
                FilterTags[i] = current;
            }
        }

        if (removed)
        {
            OnFilterStateChanged();
        }
    }

    private void RebuildFilterTagChips()
    {
        FilterTagChips.Clear();
        foreach (var tag in Tags)
        {
            FilterTagChips.Add(new FilterTagChip(tag, FilterTags.Any(t => t.Id == tag.Id)));
        }
    }

    /// <summary>
    /// Reloads the current view with the search text and filters (runs once typing pauses). Does nothing once the
    /// view model has stopped; a search the stop cancelled ends quietly, also when the database it was reading has
    /// been let go of meanwhile.
    /// </summary>
    [RelayCommand]
    private Task ApplySearchAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    /// <summary>Clears the search text (the Esc shortcut) and restores the view. Filter chips stay.</summary>
    [RelayCommand]
    private async Task ClearSearchAsync(CancellationToken cancellationToken)
    {
        if (SearchText.Length == 0)
        {
            return;
        }

        ClearSearchText();
        await LoadAsync(cancellationToken);
    }

    /// <summary>Turns off every Filter chip. The search text stays.</summary>
    [RelayCommand]
    private async Task ClearAllFiltersAsync(CancellationToken cancellationToken)
    {
        MinPriorityFilter = null;
        HasDueFilter = false;
        HasReminderFilter = false;
        FilterTags.Clear();
        OnFilterStateChanged();
        await LoadAsync(cancellationToken);
    }

    /// <summary>Shows only Tasks of at least this Priority; null or <see cref="Priority.None"/> means any.</summary>
    public async Task SetMinPriorityFilterAsync(Priority? priority, CancellationToken cancellationToken = default)
    {
        MinPriorityFilter = priority == Priority.None ? null : priority;
        await LoadAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task ToggleHasDueFilterAsync(CancellationToken cancellationToken)
    {
        HasDueFilter = !HasDueFilter;
        await LoadAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task ToggleHasReminderFilterAsync(CancellationToken cancellationToken)
    {
        HasReminderFilter = !HasReminderFilter;
        await LoadAsync(cancellationToken);
    }

    /// <summary>Adds the Tag to the filter, or removes it when it is there. All filter Tags must match.</summary>
    [RelayCommand]
    private async Task ToggleFilterTagAsync(Tag? tag, CancellationToken cancellationToken)
    {
        if (tag is null)
        {
            return;
        }

        var existing = FilterTags.FirstOrDefault(t => t.Id == tag.Id);
        if (existing is null)
        {
            FilterTags.Add(tag);
        }
        else
        {
            FilterTags.Remove(existing);
        }

        OnFilterStateChanged();
        await LoadAsync(cancellationToken);
    }
}
