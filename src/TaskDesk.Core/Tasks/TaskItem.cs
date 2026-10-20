namespace TaskDesk.Core.Tasks;

/// <summary>A Task. Named TaskItem in code to avoid clashing with System.Threading.Tasks.Task.</summary>
public sealed class TaskItem
{
    public Guid Id { get; set; }
    public Guid ListId { get; set; }
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public Priority Priority { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedUtc { get; set; }

    /// <summary>Due date: a local calendar date, not an instant.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>Due time: a local wall-clock time; only allowed when <see cref="DueDate"/> is set.</summary>
    public TimeOnly? DueTime { get; set; }

    /// <summary>The planned Reminder instant (UTC), independent of Due. Never changed by a Snooze.</summary>
    public DateTime? ReminderUtc { get; set; }

    /// <summary>
    /// The end of the Task's Snooze (UTC): nothing notifies before it, and the Task notifies once when it ends.
    /// Stays recorded after it ends.
    /// </summary>
    public DateTime? SnoozedUntilUtc { get; set; }

    /// <summary>
    /// "Notified through" (UTC): the latest moment the Task has notified for, or counts as notified. Only a
    /// moment after it can make the Task notify. Null means nothing has been notified yet.
    /// </summary>
    public DateTime? NotifiedThroughUtc { get; set; }

    /// <summary>How the Task repeats, or null. Requires a <see cref="DueDate"/>. Never shared between Tasks.</summary>
    public Recurrence? Recurrence { get; set; }

    /// <summary>Shared by all Occurrences of one Series; assigned when the first next Occurrence is produced.</summary>
    public Guid? SeriesId { get; set; }

    /// <summary>
    /// The Occurrence this one produced on completion or Skip. Once set, completing this Task again never
    /// produces another. Not a foreign key: the next Occurrence may be deleted later.
    /// </summary>
    public Guid? NextOccurrenceId { get; set; }

    public double SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    /// <summary>Tags, loaded only by queries that include them.</summary>
    public List<TaskDesk.Core.Tags.Tag> Tags { get; set; } = [];

    /// <summary>Subtasks in their manual order, loaded only by queries that include them.</summary>
    public List<Subtask> Subtasks { get; set; } = [];
}
