namespace TaskDesk.Core.Reminders;

/// <summary>How a Reminder's moment is set.</summary>
public enum ReminderKind
{
    /// <summary>A fixed date and time, independent of the Due.</summary>
    Fixed = 0,
}

/// <summary>
/// One planned moment at which the owner is notified about a Task. Owned by its Task and deleted with it. The
/// notification state (Snooze, "notified through") lives on the Task, not here (ADR 0006).
/// </summary>
public sealed class Reminder
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public ReminderKind Kind { get; set; }

    /// <summary>For a <see cref="ReminderKind.Fixed"/> Reminder, its instant (UTC).</summary>
    public DateTime? AtUtc { get; set; }

    /// <summary>What this Reminder is set to, without its identity.</summary>
    public ReminderValue Value => new(Kind, AtUtc);
}

/// <summary>What a Reminder is set to: its kind and, for a Fixed Reminder, its instant (UTC).</summary>
public sealed record ReminderValue(ReminderKind Kind, DateTime? AtUtc)
{
    /// <summary>A Reminder at a fixed instant.</summary>
    public static ReminderValue Fixed(DateTime atUtc) =>
        new(ReminderKind.Fixed, DateTime.SpecifyKind(atUtc, DateTimeKind.Utc));
}
