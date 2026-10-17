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

    /// <summary>The Due as shown on the chip ("Today 18:00", "Thu", "Thu 15 Oct"), or empty.</summary>
    [ObservableProperty]
    private string _dueText = "";

    /// <summary>Whether the Due is Overdue, due today, later or absent; the chip's colour follows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDue))]
    private DueState _dueState;

    /// <summary>True when the row has a Due, so its chip shows.</summary>
    public bool HasDue => DueState != DueState.None;

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
        RefreshDue();
    }

    /// <summary>
    /// Re-evaluates <see cref="IsOverdue"/>, <see cref="DueState"/> and <see cref="DueText"/> at the current
    /// time (time passes; the Task did not change).
    /// </summary>
    internal void RefreshDue()
    {
        var now = _clock.GetUtcNow();
        var zone = _clock.LocalTimeZone;
        IsOverdue = DueCalculator.IsOverdue(Item.DueDate, Item.DueTime, Item.IsCompleted, now, zone);
        if (Item.DueDate is not { } date)
        {
            DueText = "";
            DueState = DueState.None;
            return;
        }

        var today = DueCalculator.LocalToday(now, zone);
        DueText = DueDisplay.Format(date, Item.DueTime, today);
        DueState = IsOverdue ? DueState.Overdue : date == today ? DueState.Today : DueState.Later;
    }

    internal void RefreshSubtaskProgress() => SubtaskProgress = Core.Tasks.SubtaskProgress.Format(Subtasks);
}
