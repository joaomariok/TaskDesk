using CommunityToolkit.Mvvm.ComponentModel;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

/// <summary>A Task as shown in the List: the last saved state of a <see cref="TaskItem"/>.</summary>
public sealed partial class TaskRowViewModel : ObservableObject
{
    private readonly TimeProvider _clock;

    /// <summary>How many Tags the row shows as chips; the rest collapse into "+N".</summary>
    public const int MaxVisibleTags = 2;

    /// <param name="item">The Task.</param>
    /// <param name="clock">The clock the Due state is judged by.</param>
    /// <param name="list">The List the Task belongs to, when known.</param>
    /// <param name="showListName">True in Smart Views and a Tag's page, where the List's name is a caption.</param>
    public TaskRowViewModel(
        TaskItem item, TimeProvider? clock = null, TaskList? list = null, bool showListName = false)
    {
        _clock = clock ?? TimeProvider.System;
        ListName = list?.Name ?? "";
        ListColorHex = list?.ColorHex;
        ShowListName = showListName && ListName.Length > 0;
        Id = item.Id;
        Item = item;
        Apply(item);
    }

    public Guid Id { get; }

    /// <summary>The name of the List the Task belongs to, or empty when unknown.</summary>
    public string ListName { get; }

    /// <summary>The List's colour as "#RRGGBB", or null when the List has none.</summary>
    public string? ListColorHex { get; }

    /// <summary>True when the List has a colour, so the row shows its stripe.</summary>
    public bool HasListColor => ListColorHex is not null;

    /// <summary>True when the row captions the Task with its List's name (never inside a List).</summary>
    public bool ShowListName { get; }

    /// <summary>The most recently saved state of the Task.</summary>
    public TaskItem Item { get; private set; }

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PriorityMarker), nameof(HasPriorityFlag), nameof(PriorityLabel))]
    private Priority _priority;

    [ObservableProperty]
    private bool _isCompleted;

    /// <summary>The Tags shown as chips on the row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleTags), nameof(TagOverflowCount), nameof(HasTagOverflow),
        nameof(TagOverflowText))]
    private IReadOnlyList<Tag> _tags = [];

    /// <summary>The first <see cref="MaxVisibleTags"/> Tags, shown as chips.</summary>
    public IReadOnlyList<Tag> VisibleTags => Tags.Count <= MaxVisibleTags ? Tags : [.. Tags.Take(MaxVisibleTags)];

    /// <summary>How many Tags are not shown as chips.</summary>
    public int TagOverflowCount => Math.Max(0, Tags.Count - MaxVisibleTags);

    public bool HasTagOverflow => TagOverflowCount > 0;

    /// <summary>"+N" for the Tags beyond the chips, or empty.</summary>
    public string TagOverflowText => HasTagOverflow ? $"+{TagOverflowCount}" : "";

    /// <summary>True for Low, Medium and High: the row shows the Priority flag.</summary>
    public bool HasPriorityFlag => Priority != Priority.None;

    /// <summary>The flag's accessible name and tooltip ("High priority"), or empty for None.</summary>
    public string PriorityLabel => HasPriorityFlag ? $"{Priority} priority" : "";

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
