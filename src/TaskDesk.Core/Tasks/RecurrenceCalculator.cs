namespace TaskDesk.Core.Tasks;

/// <summary>The next-Due rules of a Recurrence (SPEC 5), pure and in one place.</summary>
public static class RecurrenceCalculator
{
    /// <summary>
    /// The Due date of the next Occurrence, counted from <paramref name="currentDue"/> or from
    /// <paramref name="completionDate"/> (the local date of the completion or Skip) per the Anchor.
    /// The result is always strictly after <paramref name="currentDue"/>. Returns null when it would be
    /// after the end date (the Series has ended).
    /// </summary>
    /// <exception cref="ArgumentException">The Recurrence is not valid.</exception>
    public static DateOnly? GetNextDueDate(Recurrence recurrence, DateOnly currentDue, DateOnly completionDate)
    {
        ArgumentNullException.ThrowIfNull(recurrence);
        recurrence.Validate();

        var next = recurrence.Anchor == RecurrenceAnchor.DueDate ? Step(recurrence, currentDue)
            : recurrence.Frequency is RecurrenceFrequency.Monthly or RecurrenceFrequency.Yearly
                ? FromCompletion(recurrence, completionDate)
                : Step(recurrence, completionDate);

        // Completed early with a completion-date Anchor: keep stepping until after the current Due.
        // Every step moves strictly forward, so this ends.
        while (next <= currentDue)
        {
            next = Step(recurrence, next);
        }

        return recurrence.EndDate is { } end && next > end ? null : next;
    }

    // Monthly and Yearly counted from the completion date: the first occurrence of the day on or after
    // the completion date (this month or year, else the next), then Interval - 1 more months or years.
    private static DateOnly FromCompletion(Recurrence r, DateOnly completion)
    {
        var day = r.DayOfMonth!.Value;
        if (r.Frequency == RecurrenceFrequency.Monthly)
        {
            var month = new DateOnly(completion.Year, completion.Month, 1);
            if (Clamped(month, day) < completion)
            {
                month = month.AddMonths(1);
            }

            return Clamped(month.AddMonths(r.Interval - 1), day);
        }

        var year = completion.Year;
        if (Clamped(new DateOnly(year, r.Month!.Value, 1), day) < completion)
        {
            year++;
        }

        return Clamped(new DateOnly(year + r.Interval - 1, r.Month!.Value, 1), day);
    }

    // One application of the rule: always strictly after `from`.
    private static DateOnly Step(Recurrence r, DateOnly from) => r.Frequency switch
    {
        RecurrenceFrequency.Daily => from.AddDays(r.Interval),
        RecurrenceFrequency.Weekly => NextWeekly(r, from),
        RecurrenceFrequency.Monthly => Clamped(new DateOnly(from.Year, from.Month, 1).AddMonths(r.Interval), r.DayOfMonth!.Value),
        RecurrenceFrequency.Yearly => Clamped(new DateOnly(from.Year + r.Interval, r.Month!.Value, 1), r.DayOfMonth!.Value),
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    // The selected days left in the current Monday-based week, else the first selected day Interval weeks on.
    private static DateOnly NextWeekly(Recurrence r, DateOnly from)
    {
        var weekStart = from.AddDays(-(((int)from.DayOfWeek + 6) % 7));
        for (var day = from.AddDays(1); day <= weekStart.AddDays(6); day = day.AddDays(1))
        {
            if (r.DaysOfWeek.HasFlag(day.DayOfWeek.ToRecurrenceDay()))
            {
                return day;
            }
        }

        var week = weekStart.AddDays(7 * r.Interval);
        for (var offset = 0; ; offset++)
        {
            var day = week.AddDays(offset);
            if (r.DaysOfWeek.HasFlag(day.DayOfWeek.ToRecurrenceDay()))
            {
                return day; // Validate guarantees at least one selected day
            }
        }
    }

    // The day in the month of `firstOfMonth`, clamped to its last day (31 in April → 30, Feb 29 → Feb 28).
    private static DateOnly Clamped(DateOnly firstOfMonth, int dayOfMonth) => new(
        firstOfMonth.Year, firstOfMonth.Month,
        Math.Min(dayOfMonth, DateTime.DaysInMonth(firstOfMonth.Year, firstOfMonth.Month)));
}
