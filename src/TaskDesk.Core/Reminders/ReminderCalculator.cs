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
    /// <param name="rules">The local rules the Due is read in; <see cref="MomentRules.Utc"/> when null.</param>
    public static DateTime? DueInstant(TaskItem task, DateTimeOffset now, MomentRules? rules = null)
    {
        var nowUtc = now.UtcDateTime;
        if (task.IsCompleted || task.SnoozedUntilUtc > nowUtc)
        {
            return null;
        }

        DateTime? latest = null;
        foreach (var moment in Moments(task, rules ?? MomentRules.Utc))
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
    public static bool IsDue(TaskItem task, DateTimeOffset now, MomentRules? rules = null) =>
        DueInstant(task, now, rules) is not null;

    // The moments a Task notifies at.
    private static IEnumerable<DateTime> Moments(TaskItem task, MomentRules rules)
    {
        foreach (var reminder in task.Reminders)
        {
            if (MomentOf(reminder, task, rules) is { } moment)
            {
                yield return moment;
            }
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
    public static IReadOnlyList<TaskItem> SelectDue(
        IEnumerable<TaskItem> tasks, DateTimeOffset now, MomentRules? rules = null) =>
        [.. tasks.Where(t => IsDue(t, now, rules)).OrderBy(t => DueInstant(t, now, rules)).ThenBy(t => t.Id)];

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
    /// The instant (UTC) a Reminder of <paramref name="task"/> fires at; null for a before-Due Reminder of a Task
    /// without a Due.
    /// </summary>
    /// <param name="rules">The local rules the Due is read in; <see cref="MomentRules.Utc"/> when null.</param>
    public static DateTime? MomentOf(Reminder reminder, TaskItem task, MomentRules? rules = null) => reminder.Kind switch
    {
        ReminderKind.Fixed => reminder.AtUtc is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null,
        ReminderKind.BeforeDue when reminder.Amount is { } amount && reminder.Unit is { } unit =>
            BeforeDue(task.DueDate, task.DueTime, amount, unit, rules ?? MomentRules.Utc),
        _ => null,
    };

    /// <summary>
    /// The Due moment (UTC): DueDate + DueTime in local time; a date-only Due counts as the default Reminder time
    /// on the Due date. Null without a Due.
    /// </summary>
    public static DateTime? DueMoment(DateOnly? dueDate, TimeOnly? dueTime, MomentRules rules) =>
        dueDate is { } date ? ToUtc(date, dueTime, rules.Zone, rules.DefaultTime) : null;

    // Minutes and hours count elapsed time from the Due moment. Days and weeks count on the local calendar,
    // keeping the wall-clock time, with ToUtc's rules for local times that are skipped or repeated.
    private static DateTime? BeforeDue(
        DateOnly? dueDate, TimeOnly? dueTime, int amount, ReminderUnit unit, MomentRules rules)
    {
        if (dueDate is not { } date || DueMoment(date, dueTime, rules) is not { } due)
        {
            return null;
        }

        switch (unit)
        {
            case ReminderUnit.Minutes:
                return due.AddMinutes(-amount);
            case ReminderUnit.Hours:
                return due.AddHours(-amount);
            case ReminderUnit.Days or ReminderUnit.Weeks:
                var days = unit == ReminderUnit.Weeks ? amount * 7 : amount;
                return ToUtc(date.AddDays(-days), dueTime ?? rules.DefaultTime, rules.Zone);
            default:
                return null;
        }
    }

    /// <summary>
    /// Adds a Reminder to the Task. Like every change to a Reminder, it clears any Snooze and counts moments
    /// already past at <paramref name="now"/> as notified (<see cref="MarkPastNotified"/>), so a Reminder set in
    /// the past never fires. Returns the new Reminder.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The Task already has this Reminder, the value is incomplete, or it is before Due on a Task without a Due.
    /// </exception>
    public static Reminder AddReminder(TaskItem task, ReminderValue value, DateTimeOffset now, MomentRules? rules = null)
    {
        Validate(task, value);
        RefuseDuplicate(task, value, except: null);
        var reminder = new Reminder
        {
            Id = Guid.CreateVersion7(),
            TaskId = task.Id,
            Kind = value.Kind,
            AtUtc = value.AtUtc,
            Amount = value.Amount,
            Unit = value.Unit,
        };
        task.Reminders.Add(reminder);
        Order(task, rules);
        Changed(task, now);
        return reminder;
    }

    /// <summary>
    /// Changes a Reminder of the Task, under the rules of <see cref="AddReminder"/>: a Reminder moved to a later
    /// moment fires there even if it had fired before. Returns false, changing nothing, when the Reminder
    /// already has this value.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The Task has no Reminder with this id.</exception>
    /// <exception cref="ArgumentException">Another Reminder of the Task has this value, or it is incomplete.</exception>
    public static bool UpdateReminder(
        TaskItem task, Guid reminderId, ReminderValue value, DateTimeOffset now, MomentRules? rules = null)
    {
        var reminder = Find(task, reminderId);
        Validate(task, value);
        if (reminder.Value == value)
        {
            return false;
        }

        RefuseDuplicate(task, value, except: reminder);
        reminder.Kind = value.Kind;
        reminder.AtUtc = value.AtUtc;
        reminder.Amount = value.Amount;
        reminder.Unit = value.Unit;
        Order(task, rules);
        Changed(task, now);
        return true;
    }

    /// <summary>
    /// Removes a Reminder from the Task, under the rules of <see cref="AddReminder"/>. Returns the removed
    /// Reminder.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The Task has no Reminder with this id.</exception>
    public static Reminder RemoveReminder(TaskItem task, Guid reminderId, DateTimeOffset now)
    {
        var reminder = Find(task, reminderId);
        task.Reminders.Remove(reminder);
        Changed(task, now);
        return reminder;
    }

    /// <summary>
    /// Sets the Task's Due. Before-Due Reminders move with it, and like a change to a Reminder it counts moments
    /// already past at <paramref name="now"/> as notified (<see cref="MarkPastNotified"/>): moments moved into the
    /// past never fire, moments moved into the future do. Clearing the Due removes the before-Due Reminders, which
    /// have nothing to count from; fixed Reminders stay. A Task with no moment that depends on the Due is left
    /// like any other field edit, so a missed fixed Reminder still fires. Returns the removed Reminders.
    /// </summary>
    public static IReadOnlyList<Reminder> ChangeDue(
        TaskItem task, DateOnly? dueDate, TimeOnly? dueTime, DateTimeOffset now)
    {
        var movesMoments = task.Reminders.Exists(r => r.Kind == ReminderKind.BeforeDue);
        task.DueDate = dueDate;
        task.DueTime = dueTime;
        List<Reminder> removed = dueDate is null ? task.Reminders.FindAll(r => r.Kind == ReminderKind.BeforeDue) : [];
        task.Reminders.RemoveAll(removed.Contains);
        if (movesMoments)
        {
            MarkPastNotified(task, now);
        }

        return removed;
    }

    /// <summary>Puts the Task's Reminders in the order they fire, the first one first.</summary>
    /// <param name="rules">The local rules the Due is read in; <see cref="MomentRules.Utc"/> when null.</param>
    public static void Order(TaskItem task, MomentRules? rules = null) =>
        task.Reminders.Sort((a, b) =>
        {
            var byMoment = Nullable.Compare(MomentOf(a, task, rules), MomentOf(b, task, rules));
            return byMoment != 0 ? byMoment : a.Id.CompareTo(b.Id);
        });

    private static Reminder Find(TaskItem task, Guid reminderId) =>
        task.Reminders.Find(r => r.Id == reminderId)
        ?? throw new KeyNotFoundException($"Reminder {reminderId} does not exist.");

    private static void Validate(TaskItem task, ReminderValue value)
    {
        switch (value.Kind)
        {
            case ReminderKind.Fixed when value.AtUtc is not null && value.Amount is null && value.Unit is null:
                return;
            case ReminderKind.BeforeDue when IsValidBeforeDue(value):
                if (task.DueDate is null)
                {
                    throw new ArgumentException("A Reminder before Due needs a Due.");
                }

                return;
            case ReminderKind.BeforeDue:
                throw new ArgumentException(
                    $"A Reminder before Due needs a whole number from {ReminderValue.MinAmount} to "
                    + $"{ReminderValue.MaxAmount} of minutes, hours, days or weeks.");
            default:
                throw new ArgumentException("A Reminder needs a date and time.");
        }
    }

    /// <summary>
    /// Whether a value is a complete before-Due Reminder: an amount from 1 to 999, a known unit and no instant.
    /// </summary>
    public static bool IsValidBeforeDue(ReminderValue value) =>
        value is
        {
            Kind: ReminderKind.BeforeDue, AtUtc: null,
            Amount: >= ReminderValue.MinAmount and <= ReminderValue.MaxAmount, Unit: { } unit,
        }
        && Enum.IsDefined(unit);

    private static void RefuseDuplicate(TaskItem task, ReminderValue value, Reminder? except)
    {
        if (task.Reminders.Any(r => r != except && r.Value == value))
        {
            throw new ArgumentException("The Task already has this Reminder.");
        }
    }

    // A change to a Reminder clears the Snooze (as it did with a single Reminder) and applies the past-moment rule.
    private static void Changed(TaskItem task, DateTimeOffset now)
    {
        task.SnoozedUntilUtc = null;
        MarkPastNotified(task, now);
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
