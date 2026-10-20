using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

/// <summary>
/// Asks the owner things the Lists navigation needs; implemented by the UI. Asynchronous because WinUI dialogs are.
/// </summary>
public interface IListDialogs
{
    /// <summary>True when the owner confirms deleting the List and its Tasks.</summary>
    Task<bool> ConfirmDeleteAsync(string listName, int taskCount);

    /// <summary>The new name typed by the owner, or null when cancelled.</summary>
    Task<string?> PromptNameAsync(string currentName);
}

public sealed partial class ListItemViewModel(ListSummary summary) : ObservableObject
{
    public Guid Id { get; } = summary.List.Id;
    public bool IsDefault { get; } = summary.List.IsDefault;

    [ObservableProperty]
    private string _name = summary.List.Name;

    [ObservableProperty]
    private string? _colorHex = summary.List.ColorHex;

    [ObservableProperty]
    private int _openTaskCount = summary.OpenTaskCount;
}

/// <summary>An entry of the sidebar the owner can open: a Smart View, a List or a Tag.</summary>
public abstract record SidebarEntry
{
    private SidebarEntry()
    {
    }

    /// <summary>A Smart View.</summary>
    public sealed record SmartViewEntry(SmartView View) : SidebarEntry;

    /// <summary>A List, by its id.</summary>
    public sealed record ListEntry(Guid ListId) : SidebarEntry;

    /// <summary>A Tag.</summary>
    public sealed record TagEntry(Tag Tag) : SidebarEntry;
}

/// <summary>
/// The left navigation: the Lists with their commands, and the entry it shows as selected, which follows the main
/// view model's page (the only page state).
/// </summary>
public sealed partial class SidebarViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly IListService _listService;
    private readonly IListDialogs _dialogs;

    /// <param name="main">The view model whose page the sidebar shows as selected and opens entries on.</param>
    /// <param name="listService">The Lists' use cases.</param>
    /// <param name="dialogs">Asks the owner for a List's name and to confirm a delete.</param>
    public SidebarViewModel(MainViewModel main, IListService listService, IListDialogs dialogs)
    {
        _main = main;
        _listService = listService;
        _dialogs = dialogs;
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Page))
            {
                OnPropertyChanged(nameof(SelectedSmartView));
                OnPropertyChanged(nameof(SelectedList));
                OnPropertyChanged(nameof(SelectedTag));
            }
        };
    }

    /// <summary>
    /// A sidebar without Lists, for a window built without them: it selects and opens Smart Views and Tags, and
    /// never has a List to select.
    /// </summary>
    public static SidebarViewModel WithoutLists(MainViewModel main) => new(main, NoLists.Instance, NoLists.Instance);

    public ObservableCollection<ListItemViewModel> Lists { get; } = [];

    /// <summary>The Smart View whose page is shown, or null on a List's or a Tag's page.</summary>
    public SmartView? SelectedSmartView => _main.CurrentView;

    /// <summary>
    /// The List whose page is shown, or null on a Smart View or a Tag's page. A page with no id or the id of a List
    /// that is gone selects the Default List (see <see cref="ListOrDefault"/>).
    /// </summary>
    public ListItemViewModel? SelectedList => _main.IsListPage ? ListOrDefault(_main.CurrentListId) : null;

    /// <summary>
    /// The current instance of the Tag whose page is shown (the one in the reloaded Tags), or null on a List's page
    /// or a Smart View.
    /// </summary>
    public Tag? SelectedTag =>
        _main.TagFilter is { } filter ? _main.Tags.FirstOrDefault(t => t.Id == filter.Id) : null;

    /// <summary>
    /// Opens an entry's page. Opening a List ends the search, also when it is the List already shown, which shows
    /// it again; opening the Smart View or the Tag already shown does nothing. Filters stay on.
    /// </summary>
    public Task OpenAsync(SidebarEntry entry, CancellationToken cancellationToken = default) => entry switch
    {
        SidebarEntry.SmartViewEntry { View: var view } when _main.CurrentView != view =>
            _main.ShowSmartViewAsync(view, cancellationToken),
        SidebarEntry.ListEntry { ListId: var listId } => _main.ShowListAsync(listId, cancellationToken),
        SidebarEntry.TagEntry { Tag: var tag } when _main.TagFilter?.Id != tag.Id =>
            _main.FilterByTagAsync(tag, cancellationToken),
        _ => Task.CompletedTask,
    };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateListCommand))]
    private string _newListName = "";

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Raised after a List was renamed, recoloured or deleted, so the views that show its name or colour (the page
    /// heading, the rows' stripes and captions) reload, and a page on a deleted List falls back to the Default List.
    /// </summary>
    public event EventHandler? ListEdited;

    // The load and the commands stop with the view model (see StopAsync).
    private readonly ViewModelLifetime _lifetime = new();

    /// <summary>
    /// Stops the view model's work: from now on the load and the commands do nothing, and work still running is
    /// cancelled and ends quietly. Idempotent; the stop itself happens before this returns, and the task completes
    /// once the cancelled work has ended, however it ended.
    /// </summary>
    public Task StopAsync() => _lifetime.StopAsync();

    /// <summary>Loads the Lists with their open-Task counts; does nothing once stopped.</summary>
    public Task LoadAsync(CancellationToken cancellationToken = default)
        => _lifetime.RunAsync(ReloadAsync, cancellationToken);

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var summaries = await _listService.GetListsAsync(cancellationToken);
        Lists.Clear();
        foreach (var summary in summaries)
        {
            Lists.Add(new ListItemViewModel(summary));
        }

        // The selected List is one of the new rows now, or the Default List if the page's List is gone.
        OnPropertyChanged(nameof(SelectedList));
    }

    [RelayCommand(CanExecute = nameof(CanCreateList))]
    private Task CreateListAsync(CancellationToken cancellationToken)
        => GuardedAsync(() => _lifetime.RunAsync(async token =>
        {
            if (!CanCreateList())
            {
                return;
            }

            await _listService.CreateListAsync(NewListName, token);
            NewListName = "";
            await ReloadAsync(token);
        }, cancellationToken));

    [RelayCommand]
    private Task RenameAsync(ListItemViewModel? item, CancellationToken cancellationToken)
        => GuardedAsync(async () =>
        {
            var name = item is null ? null : await _dialogs.PromptNameAsync(item.Name);
            if (item is null || name is null)
            {
                return;
            }

            // The dialog stays outside the lifetime, so a stop never waits on the owner.
            await _lifetime.RunAsync(async token =>
            {
                await _listService.RenameListAsync(item.Id, name, token);
                await ReloadAsync(token);
                ListEdited?.Invoke(this, EventArgs.Empty);
            }, cancellationToken);
        });

    /// <param name="item">The List to colour.</param>
    /// <param name="colorHex">#RRGGBB, or empty/null to clear the colour.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public Task SetColorAsync(ListItemViewModel item, string? colorHex, CancellationToken cancellationToken = default)
        => GuardedAsync(() => _lifetime.RunAsync(async token =>
        {
            await _listService.SetListColorAsync(item.Id, string.IsNullOrEmpty(colorHex) ? null : colorHex, token);
            await ReloadAsync(token);
            ListEdited?.Invoke(this, EventArgs.Empty);
        }, cancellationToken));

    [RelayCommand]
    private Task DeleteAsync(ListItemViewModel? item, CancellationToken cancellationToken)
        => GuardedAsync(async () =>
        {
            if (item is null || item.IsDefault || !await _dialogs.ConfirmDeleteAsync(item.Name, item.OpenTaskCount))
            {
                return;
            }

            // The dialog stays outside the lifetime, so a stop never waits on the owner.
            await _lifetime.RunAsync(async token =>
            {
                await _listService.DeleteListAsync(item.Id, token);
                await ReloadAsync(token);
                ListEdited?.Invoke(this, EventArgs.Empty);
            }, cancellationToken);
        });

    /// <summary>The List with this id, or the Default List when there is none (or the id is null).</summary>
    public ListItemViewModel? ListOrDefault(Guid? listId) =>
        Lists.FirstOrDefault(l => l.Id == listId) ?? Lists.FirstOrDefault(l => l.IsDefault);

    /// <summary>
    /// Moves a List into the gap <paramref name="gap"/> (0 is before the first List, <c>Lists.Count</c> after the
    /// last) and saves the new order. Its own gap (its index or the one after) and a gap out of range change nothing.
    /// </summary>
    public Task MoveToGapAsync(ListItemViewModel item, int gap, CancellationToken cancellationToken = default)
        => GuardedAsync(() => _lifetime.RunAsync(async token =>
        {
            var from = Lists.IndexOf(item);
            if (from < 0 || gap < 0 || gap > Lists.Count || gap == from || gap == from + 1)
            {
                return;
            }

            var ids = Lists.Select(l => l.Id).ToList();
            ids.RemoveAt(from);
            ids.Insert(gap > from ? gap - 1 : gap, item.Id);

            await _listService.ReorderListsAsync(ids, token);
            await ReloadAsync(token);
        }, cancellationToken));

    private bool CanCreateList() => ListName.IsValid(NewListName);

    // Shows a rejected edit as the error; once stopped, a command asks nothing and changes nothing.
    private async Task GuardedAsync(Func<Task> action)
    {
        if (_lifetime.IsStopped)
        {
            return;
        }

        ErrorMessage = null;
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            ErrorMessage = ex.Message;
        }
    }

    // The Lists of a sidebar that shows none: there are none to load, and no command reaches them.
    private sealed class NoLists : IListService, IListDialogs
    {
        public static NoLists Instance { get; } = new();

        public Task<IReadOnlyList<ListSummary>> GetListsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ListSummary>>([]);

        public Task<TaskList> CreateListAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RenameListAsync(Guid listId, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetListColorAsync(Guid listId, string? colorHex, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ReorderListsAsync(IReadOnlyList<Guid> orderedListIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteListAsync(Guid listId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> ConfirmDeleteAsync(string listName, int taskCount) => Task.FromResult(false);

        public Task<string?> PromptNameAsync(string currentName) => Task.FromResult<string?>(null);
    }
}
