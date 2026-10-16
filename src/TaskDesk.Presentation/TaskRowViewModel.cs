using CommunityToolkit.Mvvm.ComponentModel;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

/// <summary>A Task as shown in the List: the last saved state of a <see cref="TaskItem"/>.</summary>
public sealed partial class TaskRowViewModel : ObservableObject
{
    public TaskRowViewModel(TaskItem item)
    {
        Id = item.Id;
        Item = item;
        Apply(item);
    }

    public Guid Id { get; }

    /// <summary>The most recently saved state of the Task.</summary>
    public TaskItem Item { get; private set; }

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PriorityMarker))]
    private Priority _priority;

    [ObservableProperty]
    private bool _isCompleted;

    /// <summary>The Tags shown as chips on the row.</summary>
    [ObservableProperty]
    private IReadOnlyList<Tag> _tags = [];

    /// <summary>The Subtasks in their manual order; the source of the progress shown on the row.</summary>
    public List<Subtask> Subtasks { get; } = [];

    /// <summary>"done/total" of the Subtasks, or empty when there are none. Display only.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubtasks))]
    private string _subtaskProgress = "";

    public bool HasSubtasks => SubtaskProgress.Length > 0;

    /// <summary>The marker on the row: nothing, "!", "!!" or "!!!".</summary>
    public string PriorityMarker => new('!', (int)Priority);

    internal void Apply(TaskItem item)
    {
        Item = item;
        Title = item.Title;
        Priority = item.Priority;
        IsCompleted = item.IsCompleted;
        Tags = item.Tags;
        Subtasks.Clear();
        Subtasks.AddRange(item.Subtasks);
        RefreshSubtaskProgress();
    }

    internal void RefreshSubtaskProgress() => SubtaskProgress = Core.Tasks.SubtaskProgress.Format(Subtasks);
}
