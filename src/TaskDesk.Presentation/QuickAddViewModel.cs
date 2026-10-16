using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

/// <summary>Backs the global quick-add window: a title, a List (Default List preselected) and an optional due date.</summary>
public sealed partial class QuickAddViewModel(ITaskCreator creator, IListService lists) : ObservableObject
{
    public ObservableCollection<TaskList> Lists { get; } = [];

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private TaskList? _selectedList;

    /// <summary>Optional. Saved as the Task's Due date (the calendar date only; the time of day is ignored).</summary>
    [ObservableProperty]
    private DateTime? _dueDate;

    /// <summary>Raised after a Task was saved.</summary>
    public event Action? TaskCreated;

    /// <summary>Raised when the window should close (after a save, or on cancel).</summary>
    public event Action? CloseRequested;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Lists.Clear();
        foreach (var summary in await lists.GetListsAsync(cancellationToken))
        {
            Lists.Add(summary.List);
        }

        SelectedList = Lists.FirstOrDefault(l => l.IsDefault) ?? Lists.FirstOrDefault();
    }

    /// <summary>Saves the Task and closes. An empty title or missing List keeps the window open.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Title) || SelectedList is null)
        {
            return;
        }

        DateOnly? due = DueDate is { } picked ? DateOnly.FromDateTime(picked) : null;
        await creator.CreateTaskInListAsync(Title, SelectedList.Id, due);
        TaskCreated?.Invoke();
        CloseRequested?.Invoke();
    }

    /// <summary>Closes without saving (Esc or losing focus).</summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}
