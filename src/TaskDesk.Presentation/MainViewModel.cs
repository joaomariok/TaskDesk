using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

public sealed partial class MainViewModel(ITaskService taskService, ITagService tagService) : ObservableObject
{
    public ObservableCollection<TaskItem> Tasks { get; } = [];

    public ObservableCollection<Tag> Tags { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    private string _quickAddTitle = "";

    /// <summary>The Tag the Task list is filtered by, or null to show the Default List.</summary>
    [ObservableProperty]
    private Tag? _tagFilter;

    // Positions in a Tag-filtered view are not List positions, so reordering is off there.
    partial void OnTagFilterChanged(Tag? value) => IsReorderEnabled = value is null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveTagCommand))]
    private TaskItem? _selectedTask;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    private string _newTagName = "";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var selectedId = SelectedTask?.Id;
        Tags.Clear();
        foreach (var tag in await tagService.ListTagsAsync(cancellationToken))
        {
            Tags.Add(tag);
        }

        if (TagFilter is not null)
        {
            // Keep the filter on the current instance of its Tag, or drop it if the Tag was deleted.
            var filterId = TagFilter.Id;
            TagFilter = Tags.FirstOrDefault(t => t.Id == filterId);
        }

        var tasks = TagFilter is null
            ? await taskService.ListDefaultListTasksAsync(cancellationToken)
            : await tagService.ListTasksByTagAsync(TagFilter.Id, cancellationToken);
        Tasks.Clear();
        foreach (var task in tasks)
        {
            Tasks.Add(task);
        }

        SelectedTask = Tasks.FirstOrDefault(t => t.Id == selectedId);
    }

    /// <summary>Filters Tasks by the Tag, or shows the Default List again when null.</summary>
    public async Task FilterByTagAsync(Tag? tag, CancellationToken cancellationToken = default)
    {
        TagFilter = tag;
        await LoadAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private async Task AddTaskAsync(CancellationToken cancellationToken)
    {
        if (!CanAddTask())
        {
            return; // ExecuteAsync does not honour CanExecute, so guard here too.
        }

        var task = await taskService.CreateTaskAsync(QuickAddTitle, cancellationToken);
        if (TagFilter is null)
        {
            Tasks.Add(task); // A new Task has no Tags, so it never belongs in a Tag-filtered view.
        }

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

    [RelayCommand(CanExecute = nameof(CanAddTag))]
    private async Task AddTagAsync(CancellationToken cancellationToken)
    {
        if (!CanAddTag())
        {
            return;
        }

        await tagService.AddTagToTaskAsync(SelectedTask!.Id, NewTagName.Trim(), cancellationToken);
        NewTagName = "";
        await LoadAsync(cancellationToken);
    }

    private bool CanAddTag() => SelectedTask is not null && TagName.IsValid(NewTagName?.Trim());

    [RelayCommand(CanExecute = nameof(CanRemoveTag))]
    private async Task RemoveTagAsync(Tag tag, CancellationToken cancellationToken)
    {
        if (!CanRemoveTag())
        {
            return;
        }

        await tagService.RemoveTagFromTaskAsync(SelectedTask!.Id, tag.Id, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    private bool CanRemoveTag() => SelectedTask is not null;

    /// <summary>Deletes the Tag for good. The view must ask for confirmation first; there is no undo.</summary>
    public async Task DeleteTagAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        await tagService.DeleteTagAsync(tag.Id, cancellationToken);
        await LoadAsync(cancellationToken);
    }
}
