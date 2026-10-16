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

    /// <summary>The marker on the row: nothing, "!", "!!" or "!!!".</summary>
    public string PriorityMarker => new('!', (int)Priority);

    internal void Apply(TaskItem item)
    {
        Item = item;
        Title = item.Title;
        Priority = item.Priority;
        IsCompleted = item.IsCompleted;
        Tags = item.Tags;
    }
}
