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

    private bool CanAddTask() => TaskTitle.IsValid(QuickAddTitle);
}
