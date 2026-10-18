using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Lists;

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
    [NotifyPropertyChangedFor(nameof(Initial))]
    private string _name = summary.List.Name;

    /// <summary>The first character of the name in capitals, which the narrow rail shows for the List.</summary>
    public string Initial => Name.Length == 0 ? "" : StringInfo.GetNextTextElement(Name).ToUpperInvariant();

    [ObservableProperty]
    private string? _colorHex = summary.List.ColorHex;

    [ObservableProperty]
    private int _openTaskCount = summary.OpenTaskCount;
}

/// <summary>The Lists section of the left navigation.</summary>
public sealed partial class ListsViewModel(IListService listService, IListDialogs dialogs) : ObservableObject
{
    public ObservableCollection<ListItemViewModel> Lists { get; } = [];

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
        var summaries = await listService.GetListsAsync(cancellationToken);
        Lists.Clear();
        foreach (var summary in summaries)
        {
            Lists.Add(new ListItemViewModel(summary));
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateList))]
    private Task CreateListAsync(CancellationToken cancellationToken)
        => GuardedAsync(() => _lifetime.RunAsync(async token =>
        {
            if (!CanCreateList())
            {
                return;
            }

            await listService.CreateListAsync(NewListName, token);
            NewListName = "";
            await ReloadAsync(token);
        }, cancellationToken));

    [RelayCommand]
    private Task RenameAsync(ListItemViewModel? item, CancellationToken cancellationToken)
        => GuardedAsync(async () =>
        {
            var name = item is null ? null : await dialogs.PromptNameAsync(item.Name);
            if (item is null || name is null)
            {
                return;
            }

            // The dialog stays outside the lifetime, so a stop never waits on the owner.
            await _lifetime.RunAsync(async token =>
            {
                await listService.RenameListAsync(item.Id, name, token);
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
            await listService.SetListColorAsync(item.Id, string.IsNullOrEmpty(colorHex) ? null : colorHex, token);
            await ReloadAsync(token);
            ListEdited?.Invoke(this, EventArgs.Empty);
        }, cancellationToken));

    [RelayCommand]
    private Task DeleteAsync(ListItemViewModel? item, CancellationToken cancellationToken)
        => GuardedAsync(async () =>
        {
            if (item is null || item.IsDefault || !await dialogs.ConfirmDeleteAsync(item.Name, item.OpenTaskCount))
            {
                return;
            }

            // The dialog stays outside the lifetime, so a stop never waits on the owner.
            await _lifetime.RunAsync(async token =>
            {
                await listService.DeleteListAsync(item.Id, token);
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

            await listService.ReorderListsAsync(ids, token);
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
}
