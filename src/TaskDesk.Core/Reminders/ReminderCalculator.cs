using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Reminders;

/// <summary>
/// The Reminder rules, in one place: which Reminders are due at an instant, the instant a Reminder fires
/// at, how a local date and time become a Reminder instant, and when a Reminder is re-armed.
/// </summary>
public static class ReminderCalculator
{
    /// <summary>The local time of a Reminder set with a date only.</summary>
    public static readonly TimeOnly DefaultReminderTime = new(9, 0);

    /// <summary>The "Snooze 10 min" duration.</summary>
    public static readonly TimeSpan ShortSnooze = TimeSpan.FromMinutes(10);

    /// <summary>The "Snooze 1 h" duration.</summary>
    public static readonly TimeSpan LongSnooze = TimeSpan.FromHours(1);

    /// <summary>More Reminders than this due at once are shown as one missed-Reminders summary (SPEC 7.4).</summary>
    public const int MissedSummaryThreshold = 3;

    /// <summary>Whether <paramref name="dueCount"/> Reminders due at once are shown as one summary.</summary>
    public static bool ShouldSummarize(int dueCount) => dueCount > MissedSummaryThreshold;

    /// <summary>The instant the Reminder fires at: the Snooze when set, otherwise the planned Reminder.</summary>
    public static DateTime? EffectiveUtc(TaskItem task) => task.SnoozedUntilUtc ?? task.ReminderUtc;

    /// <summary>
    /// Due: the Task is not completed, its Reminder has not fired yet, and the instant it fires at is
    /// <paramref name="now"/> or earlier.
    /// </summary>
    public static bool IsDue(TaskItem task, DateTimeOffset now) =>
        !task.IsCompleted
        && task.ReminderFiredUtc is null
        && EffectiveUtc(task) is { } effective
        && effective <= now.UtcDateTime;

    /// <summary>The due Reminders among <paramref name="tasks"/>, the one that fires first first.</summary>
    public static IReadOnlyList<TaskItem> SelectDue(IEnumerable<TaskItem> tasks, DateTimeOffset now) =>
        [.. tasks.Where(t => IsDue(t, now)).OrderBy(EffectiveUtc).ThenBy(t => t.Id)];

    /// <summary>
    /// The Reminder instant (UTC) for a local date and time in <paramref name="zone"/>; a date-only Reminder
    /// uses <see cref="DefaultReminderTime"/>. A local time skipped by a daylight-saving jump moves forward by
    /// the gap; a local time that happens twice takes its first occurrence.
    /// </summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly? time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time ?? DefaultReminderTime, DateTimeKind.Unspecified);
        TimeSpan offset;
        if (zone.IsInvalidTime(local))
        {
            offset = zone.GetUtcOffset(local.AddDays(-1)); // the offset in force before the jump
        }
        else if (zone.IsAmbiguousTime(local))
        {
            offset = zone.GetAmbiguousTimeOffsets(local).Max();
        }
        else
        {
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }

        return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
    }

    /// <summary>
    /// The planned Reminder of the next Occurrence (SPEC 5.1 step 3): it keeps the offset the planned Reminder
    /// had from the old Due. The Due is DueDate + DueTime, or local midnight of DueDate when there is no
    /// DueTime; the next Occurrence keeps the same DueTime. The offset is measured in local wall-clock time
    /// in <paramref name="zone"/>, so "09:00 on the Due date" stays 09:00 across a daylight-saving change.
    /// The result is converted with <see cref="ToUtc"/>'s rules for skipped and repeated local times.
    /// Takes the planned Reminder only: a Snooze never carries to the next Occurrence.
    /// </summary>
    public static DateTime CarryOver(
        DateTime plannedReminderUtc, DateOnly dueDate, TimeOnly? dueTime, DateOnly nextDueDate, TimeZoneInfo zone)
    {
        var (reminderDate, reminderTime) = ToLocal(plannedReminderUtc, zone);
        var localReminder = reminderDate.ToDateTime(reminderTime);
        var dueAt = dueTime ?? TimeOnly.MinValue;
        var offset = localReminder - dueDate.ToDateTime(dueAt);
        var nextLocal = nextDueDate.ToDateTime(dueAt) + offset;
        return ToUtc(DateOnly.FromDateTime(nextLocal), TimeOnly.FromDateTime(nextLocal), zone);
    }

    /// <summary>The local date and time in <paramref name="zone"/> of a Reminder instant.</summary>
    public static (DateOnly Date, TimeOnly Time) ToLocal(DateTime reminderUtc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(reminderUtc, DateTimeKind.Utc), zone);
        return (DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
    }

    /// <summary>
    /// Sets or clears the planned Reminder. A change re-arms it: the fired marker and any Snooze are cleared.
    /// Returns false, changing nothing, when the Reminder already has this value.
    /// </summary>
    public static bool SetPlanned(TaskItem task, DateTime? reminderUtc)
    {
        if (task.ReminderUtc == reminderUtc)
        {
            return false;
        }

        task.ReminderUtc = reminderUtc;
        task.SnoozedUntilUtc = null;
        task.ReminderFiredUtc = null;
        return true;
    }

    /// <summary>
    /// Snoozes the Reminder until <paramref name="now"/> + <paramref name="duration"/> and re-arms it. The
    /// planned Reminder is left unchanged. Returns false, changing nothing, for a completed Task.
    /// </summary>
    public static bool Snooze(TaskItem task, DateTimeOffset now, TimeSpan duration)
    {
        if (task.IsCompleted)
        {
            return false;
        }

        task.SnoozedUntilUtc = now.UtcDateTime + duration;
        task.ReminderFiredUtc = null;
        return true;
    }

    /// <summary>
    /// Clears the Snooze and re-arms the planned Reminder, so a planned Reminder already past fires again.
    /// Returns false, changing nothing, when there is no Snooze.
    /// </summary>
    public static bool ClearSnooze(TaskItem task)
    {
        if (task.SnoozedUntilUtc is null)
        {
            return false;
        }

        task.SnoozedUntilUtc = null;
        task.ReminderFiredUtc = null;
        return true;
    }
}
