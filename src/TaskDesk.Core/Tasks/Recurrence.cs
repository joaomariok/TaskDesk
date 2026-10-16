namespace TaskDesk.Core.Tasks;

public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Yearly = 3,
}

/// <summary>Which date the next Occurrence is counted from.</summary>
public enum RecurrenceAnchor
{
    /// <summary>The current Occurrence's Due date (the default).</summary>
    DueDate = 0,

    /// <summary>The local date the current Occurrence was completed or Skipped.</summary>
    CompletionDate = 1,
}

/// <summary>The days a Weekly Recurrence falls on. Weeks start on Monday.</summary>
[Flags]
public enum RecurrenceDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
}

public static class RecurrenceDaysExtensions
{
    /// <summary>The <see cref="RecurrenceDays"/> flag of a day of the week.</summary>
    public static RecurrenceDays ToRecurrenceDay(this DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => RecurrenceDays.Monday,
        DayOfWeek.Tuesday => RecurrenceDays.Tuesday,
        DayOfWeek.Wednesday => RecurrenceDays.Wednesday,
        DayOfWeek.Thursday => RecurrenceDays.Thursday,
        DayOfWeek.Friday => RecurrenceDays.Friday,
        DayOfWeek.Saturday => RecurrenceDays.Saturday,
        DayOfWeek.Sunday => RecurrenceDays.Sunday,
        _ => throw new ArgumentOutOfRangeException(nameof(day)),
    };
}

/// <summary>
/// The rule a recurring Task repeats by (SPEC 4.2), stored on each Occurrence. Fields that must not
/// drift between Occurrences (the Yearly Month and DayOfMonth) are stored, never inferred from the Due.
/// Treat it as a value: copy it with <c>with</c>, never share one instance between two Tasks.
/// </summary>
public sealed record Recurrence
{
    /// <summary>Largest allowed <see cref="Interval"/>, so date arithmetic stays far from overflow.</summary>
    public const int MaxInterval = 999;

    public RecurrenceFrequency Frequency { get; init; }

    /// <summary>"Every N days/weeks/months/years"; at least 1.</summary>
    public int Interval { get; init; } = 1;

    /// <summary>Weekly only: the selected days (at least one).</summary>
    public RecurrenceDays DaysOfWeek { get; init; }

    /// <summary>Yearly only: 1 to 12.</summary>
    public int? Month { get; init; }

    /// <summary>Monthly and Yearly: 1 to 31, valid for <see cref="Month"/> when Yearly (Feb 29 allowed).</summary>
    public int? DayOfMonth { get; init; }

    public RecurrenceAnchor Anchor { get; init; }

    /// <summary>No Occurrence is produced after this date.</summary>
    public DateOnly? EndDate { get; init; }

    public static Recurrence Daily(int interval = 1) =>
        new() { Frequency = RecurrenceFrequency.Daily, Interval = interval };

    /// <summary>The "Weekdays" preset: Monday to Friday, every week.</summary>
    public static Recurrence Weekdays() => Weekly(
        RecurrenceDays.Monday | RecurrenceDays.Tuesday | RecurrenceDays.Wednesday | RecurrenceDays.Thursday
        | RecurrenceDays.Friday);

    public static Recurrence Weekly(RecurrenceDays days, int interval = 1) =>
        new() { Frequency = RecurrenceFrequency.Weekly, Interval = interval, DaysOfWeek = days };

    public static Recurrence Monthly(int dayOfMonth, int interval = 1) =>
        new() { Frequency = RecurrenceFrequency.Monthly, Interval = interval, DayOfMonth = dayOfMonth };

    public static Recurrence Yearly(int month, int dayOfMonth, int interval = 1) => new()
    {
        Frequency = RecurrenceFrequency.Yearly, Interval = interval, Month = month, DayOfMonth = dayOfMonth,
    };

    /// <summary>Checks that the fields are consistent with the Frequency.</summary>
    /// <exception cref="ArgumentException">The Recurrence is not valid.</exception>
    public void Validate()
    {
        if (!Enum.IsDefined(Frequency) || !Enum.IsDefined(Anchor))
        {
            throw new ArgumentException("Unknown Recurrence frequency or anchor.");
        }

        if (Interval is < 1 or > MaxInterval)
        {
            throw new ArgumentException($"The interval must be between 1 and {MaxInterval}.");
        }

        const RecurrenceDays allDays = (RecurrenceDays)127;
        var weekly = Frequency == RecurrenceFrequency.Weekly;
        if (weekly ? DaysOfWeek == RecurrenceDays.None || (DaysOfWeek & ~allDays) != 0 : DaysOfWeek != RecurrenceDays.None)
        {
            throw new ArgumentException("A Weekly Recurrence needs at least one day; other frequencies none.");
        }

        var usesDay = Frequency is RecurrenceFrequency.Monthly or RecurrenceFrequency.Yearly;
        if (usesDay != DayOfMonth.HasValue || DayOfMonth is < 1 or > 31)
        {
            throw new ArgumentException("Monthly and Yearly need a day of month from 1 to 31; other frequencies none.");
        }

        var yearly = Frequency == RecurrenceFrequency.Yearly;
        if (yearly != Month.HasValue || Month is < 1 or > 12)
        {
            throw new ArgumentException("Yearly needs a month from 1 to 12; other frequencies none.");
        }

        // 2000 is a leap year, so Feb 29 is allowed.
        if (yearly && DayOfMonth > DateTime.DaysInMonth(2000, Month!.Value))
        {
            throw new ArgumentException("The day does not exist in that month.");
        }
    }
}
