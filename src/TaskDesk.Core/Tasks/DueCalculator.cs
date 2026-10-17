using TaskDesk.Core.Reminders;

namespace TaskDesk.Core.Tasks;

/// <summary>
/// The Due rules, in one place. Due is a local calendar date with an optional local time; "now" is an
/// instant converted to the given time zone. This holds the single Overdue predicate that the Today
/// view's Overdue group and the Overdue Smart View both use.
/// </summary>
public static class DueCalculator
{
    /// <summary>How many days after today the Upcoming Smart View reaches (today excluded, today + 7 included).</summary>
    public const int UpcomingDays = 7;

    /// <summary>The local calendar date of <paramref name="now"/> in <paramref name="zone"/>.</summary>
    public static DateOnly LocalToday(DateTimeOffset now, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

    /// <summary>
    /// Overdue: not completed and Due has passed. A timed Due is overdue once its time has passed (strictly
    /// after it); a date-only Due is overdue from the start of the next local day.
    /// </summary>
    public static bool IsOverdue(
        DateOnly? dueDate, TimeOnly? dueTime, bool isCompleted, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (isCompleted || dueDate is not { } date)
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var today = DateOnly.FromDateTime(local);
        if (date != today)
        {
            return date < today;
        }

        return dueTime is { } time && time < TimeOnly.FromDateTime(local);
    }

    /// <summary>Today view: not completed and Due today or earlier (Overdue ones included).</summary>
    public static bool IsInToday(DateOnly? dueDate, bool isCompleted, DateTimeOffset now, TimeZoneInfo zone) =>
        !isCompleted && dueDate is { } date && date <= LocalToday(now, zone);

    /// <summary>Upcoming view: not completed and Due in (today, today + 7].</summary>
    public static bool IsUpcoming(DateOnly? dueDate, bool isCompleted, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (isCompleted || dueDate is not { } date)
        {
            return false;
        }

        var today = LocalToday(now, zone);
        return date > today && date <= today.AddDays(UpcomingDays);
    }

    /// <summary>
    /// The next instant after <paramref name="now"/> at which <see cref="IsOverdue"/>, <see cref="IsInToday"/>
    /// or <see cref="IsUpcoming"/> can change for <paramref name="tasks"/>: the first instant an open Task
    /// with a timed Due later today is Overdue (just after its Due time), or else the next local midnight.
    /// </summary>
    public static DateTimeOffset NextChange(IEnumerable<TaskItem> tasks, DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = LocalToday(now, zone);
        var next = new DateTimeOffset(ReminderCalculator.ToUtc(today.AddDays(1), TimeOnly.MinValue, zone));
        foreach (var task in tasks)
        {
            if (task.IsCompleted || task.DueDate != today || task.DueTime is not { } time)
            {
                continue;
            }

            // Overdue strictly after the Due time, so the change is one tick later.
            var overdueFrom = new DateTimeOffset(ReminderCalculator.ToUtc(today, time, zone)).AddTicks(1);
            if (overdueFrom > now && overdueFrom < next)
            {
                next = overdueFrom;
            }
        }

        return next;
    }

    /// <summary>A Due time is only allowed together with a Due date.</summary>
    /// <exception cref="ArgumentException">A Due time without a Due date.</exception>
    public static void ValidateDue(DateOnly? dueDate, TimeOnly? dueTime)
    {
        if (dueTime is not null && dueDate is null)
        {
            throw new ArgumentException("A Due time needs a Due date.", nameof(dueTime));
        }
    }
}
