namespace TaskDesk.Core.Reminders;

/// <summary>How a Reminder's moment is set.</summary>
public enum ReminderKind
{
    /// <summary>A fixed date and time, independent of the Due.</summary>
    Fixed = 0,

    /// <summary>An amount of time before the Due; it moves with the Due.</summary>
    BeforeDue = 1,
}

/// <summary>The unit of a before-Due Reminder's amount.</summary>
public enum ReminderUnit
{
    /// <summary>Elapsed minutes.</summary>
    Minutes = 0,

    /// <summary>Elapsed hours.</summary>
    Hours = 1,

    /// <summary>Days on the local calendar, keeping the wall-clock time.</summary>
    Days = 2,

    /// <summary>Weeks on the local calendar, keeping the wall-clock time.</summary>
    Weeks = 3,
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

    /// <summary>For a <see cref="ReminderKind.BeforeDue"/> Reminder, how many units before the Due (1 to 999).</summary>
    public int? Amount { get; set; }

    /// <summary>For a <see cref="ReminderKind.BeforeDue"/> Reminder, the unit of <see cref="Amount"/>.</summary>
    public ReminderUnit? Unit { get; set; }

    /// <summary>What this Reminder is set to, without its identity.</summary>
    public ReminderValue Value => new(Kind, AtUtc, Amount, Unit);
}

/// <summary>
/// What a Reminder is set to: its kind and, for a Fixed Reminder, its instant (UTC), or for a before-Due Reminder,
/// its amount and unit.
/// </summary>
public sealed record ReminderValue(ReminderKind Kind, DateTime? AtUtc, int? Amount = null, ReminderUnit? Unit = null)
{
    /// <summary>The smallest amount of a before-Due Reminder.</summary>
    public const int MinAmount = 1;

    /// <summary>The largest amount of a before-Due Reminder.</summary>
    public const int MaxAmount = 999;

    /// <summary>A Reminder at a fixed instant.</summary>
    public static ReminderValue Fixed(DateTime atUtc) =>
        new(ReminderKind.Fixed, DateTime.SpecifyKind(atUtc, DateTimeKind.Utc));

    /// <summary>A Reminder <paramref name="amount"/> <paramref name="unit"/> before the Due.</summary>
    public static ReminderValue BeforeDue(int amount, ReminderUnit unit) =>
        new(ReminderKind.BeforeDue, null, amount, unit);
}

/// <summary>
/// The local rules a Due is read in: the time zone and the default Reminder time a date-only Due counts from.
/// Both are read when the rule runs, never stored, so a Settings or time-zone change applies at once.
/// </summary>
public sealed record MomentRules(TimeZoneInfo Zone, TimeOnly DefaultTime)
{
    /// <summary>UTC with the initial default Reminder time, for callers whose Tasks hold only Fixed Reminders.</summary>
    public static MomentRules Utc { get; } = new(TimeZoneInfo.Utc, ReminderCalculator.DefaultReminderTime);
}
