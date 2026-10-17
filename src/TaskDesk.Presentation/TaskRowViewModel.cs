using CommunityToolkit.Mvvm.ComponentModel;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

/// <summary>A Task as shown in the List: the last saved state of a <see cref="TaskItem"/>.</summary>
public sealed partial class TaskRowViewModel : ObservableObject
{
    private readonly TimeProvider _clock;

    public TaskRowViewModel(TaskItem item, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
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

    /// <summary>The Due as shown on the row ("20/10/2026" or "20/10/2026 14:30"), or empty.</summary>
    [ObservableProperty]
    private string _dueText = "";

    /// <summary>True when the Task is Overdue; the row shows its Due in red.</summary>
    [ObservableProperty]
    private bool _isOverdue;

    /// <summary>True when the Task repeats; the row shows the repeat icon.</summary>
    [ObservableProperty]
    private bool _isRecurring;

    /// <summary>The marker on the row: nothing, "!", "!!" or "!!!".</summary>
    public string PriorityMarker => new('!', (int)Priority);

    internal void Apply(TaskItem item)
    {
        Item = item;
        Title = item.Title;
        Priority = item.Priority;
        IsCompleted = item.IsCompleted;
        IsRecurring = item.Recurrence is not null;
        Tags = item.Tags;
        Subtasks.Clear();
        Subtasks.AddRange(item.Subtasks);
        RefreshSubtaskProgress();
        DueText = item.DueDate is { } date
            ? item.DueTime is { } time ? $"{date:d} {time:t}" : $"{date:d}"
            : "";
        RefreshOverdue();
    }

    /// <summary>Re-evaluates <see cref="IsOverdue"/> at the current time (time passes; the Task did not change).</summary>
    internal void RefreshOverdue() => IsOverdue = DueCalculator.IsOverdue(
        Item.DueDate, Item.DueTime, Item.IsCompleted, _clock.GetUtcNow(), _clock.LocalTimeZone);

    internal void RefreshSubtaskProgress() => SubtaskProgress = Core.Tasks.SubtaskProgress.Format(Subtasks);
}
