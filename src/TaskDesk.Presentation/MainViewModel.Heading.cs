using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

// The text at the top of the Task list: the page heading, what quick-add and search act on, and the
// empty or no-result message. The view only binds to it.
public sealed partial class MainViewModel
{
    /// <summary>The name of the Default List, which the List view shows and quick-add feeds.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageHeading))]
    [NotifyPropertyChangedFor(nameof(QuickAddTarget))]
    [NotifyPropertyChangedFor(nameof(SearchScope))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    private string _defaultListName = "Inbox";

    /// <summary>True when the page shows a List (not a Smart View or a Tag's page).</summary>
    public bool IsListPage => Page is TaskListPage.ListPage;

    // The shown List's name; the Default List's until the Lists are loaded.
    private string CurrentListName =>
        CurrentListId is { } id && _lists.TryGetValue(id, out var list) ? list.Name : DefaultListName;

    /// <summary>What the page shows: the Smart View's name, the List's name, or "#" plus the Tag's name.</summary>
    public string PageHeading => Page switch
    {
        TaskListPage.SmartViewPage page => ViewName(page.View),
        TaskListPage.TagPage page => "#" + page.Tag.Name,
        _ => CurrentListName,
    };

    /// <summary>
    /// The label of the quick-add box: a List's page adds to that List; a Smart View or a Tag's page adds to
    /// the Default List.
    /// </summary>
    public string QuickAddTarget => "Add a task to " + (IsListPage ? CurrentListName : DefaultListName);

    /// <summary>True when quick add is offered: on every page except the Completed Smart View.</summary>
    public bool ShowsQuickAdd => CurrentView != SmartView.Completed;

    /// <summary>Raised when the view should put keyboard focus in quick add (Ctrl+N).</summary>
    public event EventHandler? FocusQuickAddRequested;

    /// <summary>
    /// Ctrl+N. On the Completed Smart View, where Tasks cannot be added, it first shows the Default List;
    /// everywhere else it only asks the view to focus quick add.
    /// </summary>
    [RelayCommand]
    private async Task FocusQuickAddAsync(CancellationToken cancellationToken)
    {
        if (CurrentView == SmartView.Completed)
        {
            await ShowSmartViewAsync(null, cancellationToken);
        }

        FocusQuickAddRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The hint of the search box: search applies within the current view.</summary>
    public string SearchScope => "Search in " + PageHeading;

    /// <summary>True when there is no Task to show: nothing open, Overdue or completed.</summary>
    public bool IsPageEmpty => Tasks.Count == 0 && OverdueTasks.Count == 0 && CompletedTasks.Count == 0;

    /// <summary>The plain-text empty or no-result message, or an empty string while there are Tasks to show.</summary>
    public string EmptyMessage => IsPageEmpty ? BuildEmptyMessage() : "";

    /// <summary>True when <see cref="EmptyMessage"/> is showing.</summary>
    public bool HasEmptyMessage => IsPageEmpty;

    /// <summary>True when the empty page is the result of a search text, so "Clear search" applies.</summary>
    public bool ShowClearSearch => IsPageEmpty && SearchText.Length > 0;

    private static string ViewName(SmartView view) => view switch
    {
        SmartView.Today => "Today",
        SmartView.Upcoming => "Upcoming",
        SmartView.Overdue => "Overdue",
        SmartView.All => "All",
        SmartView.Completed => "Completed",
        _ => view.ToString(),
    };

    private string BuildEmptyMessage()
    {
        if (SearchText.Length > 0)
        {
            return $"No Tasks match '{SearchText}'";
        }

        if (IsNarrowed)
        {
            return "No Tasks match the filters.";
        }

        return CurrentView switch
        {
            SmartView.Today => "Nothing due today.",
            SmartView.Overdue => "Nothing is overdue.",
            SmartView.Upcoming => "Nothing is coming up. Tasks with a later Due date appear here.",
            SmartView.All => "No Tasks yet. Type above to add one.",
            SmartView.Completed => "No completed Tasks.",
            _ => TagFilter is { } tag
                ? $"No Tasks tagged #{tag.Name}."
                : $"No Tasks in {CurrentListName} yet. Type above to add one.",
        };
    }

    // Raised whenever the shown view, the search or the Task groups change.
    private void NotifyPageStateChanged()
    {
        OnPropertyChanged(nameof(IsListPage));
        OnPropertyChanged(nameof(PageHeading));
        OnPropertyChanged(nameof(QuickAddTarget));
        OnPropertyChanged(nameof(SearchScope));
        OnPropertyChanged(nameof(IsPageEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(HasEmptyMessage));
        OnPropertyChanged(nameof(ShowClearSearch));
    }
}
