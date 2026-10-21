using System.Globalization;
using TaskDesk.Core.Reminders;

namespace TaskDesk.Presentation;

/// <summary>
/// How a Reminder row reads: the moment it fires ("Today 09:00", "Tomorrow 09:00", "Sun 25 Oct 09:00"), and a
/// before-Due offset, compact on the row ("15 min", "2 h", "1 day") and in full for its description ("15 minutes").
/// </summary>
internal static class ReminderWording
{
    /// <summary>
    /// "Today HH:mm", "Tomorrow HH:mm", otherwise the date, with the year only when it is not the current one.
    /// Never a bare weekday.
    /// </summary>
    public static string Moment(DateOnly date, TimeOnly time, DateOnly today, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var day = (date.DayNumber - today.DayNumber) switch
        {
            0 => "Today",
            1 => "Tomorrow",
            _ => date.ToString(date.Year == today.Year ? "ddd d MMM" : "ddd d MMM yyyy", culture),
        };
        return $"{day} {time.ToString("HH:mm", culture)}";
    }

    /// <summary>The offset as the row reads it: "15 min", "2 h", "1 day", "2 weeks".</summary>
    public static string CompactOffset(int amount, ReminderUnit unit, CultureInfo? culture = null) =>
        unit switch
        {
            ReminderUnit.Minutes => $"{Number(amount, culture)} min",
            ReminderUnit.Hours => $"{Number(amount, culture)} h",
            _ => FullOffset(amount, unit, culture),
        };

    /// <summary>The offset in full words, for the tooltip and the accessible name: "15 minutes", "1 hour".</summary>
    public static string FullOffset(int amount, ReminderUnit unit, CultureInfo? culture = null)
    {
        var name = unit switch
        {
            ReminderUnit.Minutes => "minute",
            ReminderUnit.Hours => "hour",
            ReminderUnit.Days => "day",
            _ => "week",
        };
        return amount == 1 ? $"1 {name}" : $"{Number(amount, culture)} {name}s";
    }

    private static string Number(int amount, CultureInfo? culture) =>
        amount.ToString(culture ?? CultureInfo.CurrentCulture);
}
