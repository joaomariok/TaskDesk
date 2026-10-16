using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

public sealed partial class MainViewModel(ITaskService taskService) : ObservableObject
{
    public ObservableCollection<TaskItem> Tasks { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    private string _quickAddTitle = "";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Tasks.Clear();
        foreach (var task in await taskService.ListDefaultListTasksAsync(cancellationToken))
        {
            Tasks.Add(task);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private async Task AddTaskAsync(CancellationToken cancellationToken)
    {
        if (!CanAddTask())
        {
            return; // ExecuteAsync does not honour CanExecute, so guard here too.
        }

        var task = await taskService.CreateTaskAsync(QuickAddTitle, cancellationToken);
        Tasks.Add(task);
        QuickAddTitle = "";
    }

    /// <summary>
    /// Whether drag-and-drop reordering is allowed. True in a List view; Smart Views and search
    /// results must set it to false.
    /// </summary>
    [ObservableProperty]
    private bool _isReorderEnabled = true;

    /// <summary>Moves the Task at <paramref name="oldIndex"/> to <paramref name="newIndex"/> and persists it.</summary>
    public async Task MoveTaskAsync(int oldIndex, int newIndex, CancellationToken cancellationToken = default)
    {
        if (!IsReorderEnabled || oldIndex == newIndex
            || oldIndex < 0 || oldIndex >= Tasks.Count
            || newIndex < 0 || newIndex >= Tasks.Count)
        {
            return;
        }

        await taskService.MoveTaskAsync(Tasks[oldIndex].Id, newIndex, cancellationToken);
        Tasks.Move(oldIndex, newIndex);
    }

    private bool CanAddTask() => TaskTitle.IsValid(QuickAddTitle);
}
