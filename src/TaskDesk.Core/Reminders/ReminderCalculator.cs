using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Reminders;

/// <summary>
/// The Reminder rules, in one place: which Reminders are due at an instant, the instant a Reminder fires
/// at, how a local date and time become a Reminder instant, and when a Reminder is re-armed.
/// </summary>
public static class ReminderCalculator
{
    /// <summary>
    /// The local time of a Reminder set with a date only, when no default time is given (the "Default
    /// reminder time" setting starts at this value).
    /// </summary>
    public static readonly TimeOnly DefaultReminderTime = new(9, 0);

    /// <summary>The "Snooze 10 min" duration.</summary>
    public static readonly TimeSpan ShortSnooze = TimeSpan.FromMinutes(10);

    /// <summary>The "Snooze 1 h" duration.</summary>
    public static readonly TimeSpan LongSnooze = TimeSpan.FromHours(1);

    /// <summary>More Reminders than this due at once are shown as one missed-Reminders summary (SPEC 7.4).</summary>
    public const int MissedSummaryThreshold = 3;

    /// <summary>Whether <paramref name="dueCount"/> Reminders due at once are shown as one summary.</summary>
    public static bool ShouldSummarize(int dueCount) => dueCount > MissedSummaryThreshold;

    /// <summary>
    /// The due rule (SPEC 7.4, ADR 0006). A Task is due to notify at <paramref name="now"/> when it is open,
    /// not snoozed past <paramref name="now"/>, and one of its moments (the planned Reminder, the end of the
    /// Snooze) lies after "notified through" and at or before <paramref name="now"/>. Returns the latest such
    /// moment, its due instant, or null when the Task is not due.
    /// </summary>
    public static DateTime? DueInstant(TaskItem task, DateTimeOffset now)
    {
        var nowUtc = now.UtcDateTime;
        if (task.IsCompleted || task.SnoozedUntilUtc > nowUtc)
        {
            return null;
        }

        DateTime? latest = null;
        foreach (var moment in Moments(task))
        {
            if (moment <= nowUtc && (task.NotifiedThroughUtc is not { } through || moment > through)
                && (latest is null || moment > latest))
            {
                latest = moment;
            }
        }

        return latest;
    }

    /// <summary>Whether the Task is due to notify at <paramref name="now"/> (see <see cref="DueInstant"/>).</summary>
    public static bool IsDue(TaskItem task, DateTimeOffset now) => DueInstant(task, now) is not null;

    // The moments a Task notifies at.
    private static IEnumerable<DateTime> Moments(TaskItem task)
    {
        if (task.ReminderUtc is { } reminder)
        {
            yield return reminder;
        }

        if (task.SnoozedUntilUtc is { } snoozeEnd)
        {
            yield return snoozeEnd;
        }
    }

    /// <summary>
    /// The past-moment rule: moves "notified through" to at least <paramref name="now"/>, so moments already
    /// past never notify. Used by every change to what makes a Task notify, and when a Task is created.
    /// </summary>
    public static void MarkPastNotified(TaskItem task, DateTimeOffset now)
    {
        var nowUtc = now.UtcDateTime;
        if (task.NotifiedThroughUtc is not { } through || through < nowUtc)
        {
            task.NotifiedThroughUtc = nowUtc;
        }
    }

    /// <summary>The due Tasks among <paramref name="tasks"/>, the one with the earliest due instant first.</summary>
    public static IReadOnlyList<TaskItem> SelectDue(IEnumerable<TaskItem> tasks, DateTimeOffset now) =>
        [.. tasks.Where(t => IsDue(t, now)).OrderBy(t => DueInstant(t, now)).ThenBy(t => t.Id)];

    /// <summary>
    /// The Reminder instant (UTC) for a local date and time in <paramref name="zone"/>; a date-only Reminder
    /// uses <paramref name="defaultTime"/>, or <see cref="DefaultReminderTime"/> when none is given. A local time skipped by a daylight-saving jump moves forward by
    /// the gap; a local time that happens twice takes its first occurrence.
    /// </summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly? time, TimeZoneInfo zone, TimeOnly? defaultTime = null)
    {
        var local = date.ToDateTime(time ?? defaultTime ?? DefaultReminderTime, DateTimeKind.Unspecified);
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
    /// Sets or clears the planned Reminder and clears any Snooze. Moments already past at
    /// <paramref name="now"/> count as notified (<see cref="MarkPastNotified"/>), so a Reminder moved to a
    /// later moment fires there even if it had fired before, and one set in the past never fires.
    /// Returns false, changing nothing, when the Reminder already has this value.
    /// </summary>
    public static bool SetPlanned(TaskItem task, DateTime? reminderUtc, DateTimeOffset now)
    {
        if (task.ReminderUtc == reminderUtc)
        {
            return false;
        }

        task.ReminderUtc = reminderUtc;
        task.SnoozedUntilUtc = null;
        MarkPastNotified(task, now);
        return true;
    }

    /// <summary>
    /// Snoozes the Task until <paramref name="now"/> + <paramref name="duration"/>: nothing notifies before
    /// then, and the Task notifies once when it ends. The planned Reminder and "notified through" are left
    /// unchanged. Returns false, changing nothing, for a completed Task.
    /// </summary>
    public static bool Snooze(TaskItem task, DateTimeOffset now, TimeSpan duration)
    {
        if (task.IsCompleted)
        {
            return false;
        }

        task.SnoozedUntilUtc = now.UtcDateTime + duration;
        return true;
    }

    /// <summary>
    /// Clears the Snooze and resets "notified through", so the Task notifies once at the next check for its
    /// latest moment already past (a planned Reminder already past fires again). Returns false, changing
    /// nothing, when there is no Snooze.
    /// </summary>
    public static bool ClearSnooze(TaskItem task)
    {
        if (task.SnoozedUntilUtc is null)
        {
            return false;
        }

        task.SnoozedUntilUtc = null;
        task.NotifiedThroughUtc = null;
        return true;
    }
}
