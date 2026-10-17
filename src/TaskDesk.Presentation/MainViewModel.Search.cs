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
    /// <summary>The search text, matched against titles, notes and Subtask titles of the current view.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNarrowed))]
    private string _searchText = "";

    /// <summary>The lowest Priority shown, or null for any.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNarrowed))]
    private Priority? _minPriorityFilter;

    /// <summary>Only show Tasks that have a Due date.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNarrowed))]
    private bool _hasDueFilter;

    /// <summary>Only show Tasks that have a Reminder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNarrowed))]
    private bool _hasReminderFilter;

    /// <summary>The Tags a Task must all carry to be shown.</summary>
    public ObservableCollection<Tag> FilterTags { get; } = [];

    /// <summary>One chip per Tag, marking those in <see cref="FilterTags"/>.</summary>
    public ObservableCollection<FilterTagChip> FilterTagChips { get; } = [];

    /// <summary>True while a search text or any filter chip narrows the view.</summary>
    public bool IsNarrowed => !BuildSearchQuery(0).IsEmpty;

    partial void OnSearchTextChanged(string value) => OnFilterStateChanged();

    partial void OnMinPriorityFilterChanged(Priority? value) => OnFilterStateChanged();

    partial void OnHasDueFilterChanged(bool value) => OnFilterStateChanged();

    partial void OnHasReminderFilterChanged(bool value) => OnFilterStateChanged();

    private void OnFilterStateChanged()
    {
        UpdateReorderEnabled();
        NotifyPageStateChanged();
    }

    private TaskSearchQuery BuildSearchQuery(int skip) => new(
        SearchText,
        MinPriorityFilter,
        [.. FilterTags.Select(t => t.Id)],
        HasDueFilter,
        CurrentView,
        CurrentView is null ? TagFilter?.Id : null,
        skip,
        HasReminderFilter);

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
            OnPropertyChanged(nameof(IsNarrowed));
            UpdateReorderEnabled();
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

    /// <summary>Reloads the current view with the search text and filters (call after typing pauses).</summary>
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

        SearchText = "";
        await LoadAsync(cancellationToken);
    }

    /// <summary>Clears the search text and every filter chip.</summary>
    [RelayCommand]
    private async Task ClearAllFiltersAsync(CancellationToken cancellationToken)
    {
        SearchText = "";
        MinPriorityFilter = null;
        HasDueFilter = false;
        HasReminderFilter = false;
        FilterTags.Clear();
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

        OnPropertyChanged(nameof(IsNarrowed));
        UpdateReorderEnabled();
        await LoadAsync(cancellationToken);
    }
}
