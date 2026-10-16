using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Lists;

namespace TaskDesk.Presentation;

/// <summary>Asks the owner things the Lists navigation needs; implemented by the UI.</summary>
public interface IListDialogs
{
    /// <summary>True when the owner confirms deleting the List and its Tasks.</summary>
    bool ConfirmDelete(string listName, int taskCount);

    /// <summary>The new name typed by the owner, or null when cancelled.</summary>
    string? PromptName(string currentName);
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

/// <summary>The Lists section of the left navigation.</summary>
public sealed partial class ListsViewModel(IListService listService, IListDialogs dialogs) : ObservableObject
{
    public ObservableCollection<ListItemViewModel> Lists { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateListCommand))]
    private string _newListName = "";

    [ObservableProperty]
    private string? _errorMessage;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
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
        => GuardedAsync(async () =>
        {
            if (!CanCreateList())
            {
                return;
            }

            await listService.CreateListAsync(NewListName, cancellationToken);
            NewListName = "";
            await LoadAsync(cancellationToken);
        });

    [RelayCommand]
    private Task RenameAsync(ListItemViewModel? item, CancellationToken cancellationToken)
        => GuardedAsync(async () =>
        {
            var name = item is null ? null : dialogs.PromptName(item.Name);
            if (item is null || name is null)
            {
                return;
            }

            await listService.RenameListAsync(item.Id, name, cancellationToken);
            await LoadAsync(cancellationToken);
        });

    /// <param name="item">The List to colour.</param>
    /// <param name="colorHex">#RRGGBB, or empty/null to clear the colour.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public Task SetColorAsync(ListItemViewModel item, string? colorHex, CancellationToken cancellationToken = default)
        => GuardedAsync(async () =>
        {
            await listService.SetListColorAsync(
                item.Id, string.IsNullOrEmpty(colorHex) ? null : colorHex, cancellationToken);
            await LoadAsync(cancellationToken);
        });

    [RelayCommand]
    private Task DeleteAsync(ListItemViewModel? item, CancellationToken cancellationToken)
        => GuardedAsync(async () =>
        {
            if (item is null || item.IsDefault || !dialogs.ConfirmDelete(item.Name, item.OpenTaskCount))
            {
                return;
            }

            await listService.DeleteListAsync(item.Id, cancellationToken);
            await LoadAsync(cancellationToken);
        });

    /// <summary>Moves a List to the position currently held by <paramref name="target"/>.</summary>
    public Task MoveAsync(ListItemViewModel item, ListItemViewModel target, CancellationToken cancellationToken = default)
        => GuardedAsync(async () =>
        {
            var from = Lists.IndexOf(item);
            var to = Lists.IndexOf(target);
            if (from < 0 || to < 0 || from == to)
            {
                return;
            }

            var ids = Lists.Select(l => l.Id).ToList();
            ids.RemoveAt(from);
            ids.Insert(to, item.Id);

            await listService.ReorderListsAsync(ids, cancellationToken);
            await LoadAsync(cancellationToken);
        });

    private bool CanCreateList() => ListName.IsValid(NewListName);

    private async Task GuardedAsync(Func<Task> action)
    {
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
