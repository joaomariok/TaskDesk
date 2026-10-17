using System.Globalization;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Export;

/// <summary>Human-readable text for a Recurrence, used by the CSV export.</summary>
public static class RecurrenceText
{
    private static readonly (RecurrenceDays Day, string Name)[] Days =
    [
        (RecurrenceDays.Monday, "Mon"), (RecurrenceDays.Tuesday, "Tue"), (RecurrenceDays.Wednesday, "Wed"),
        (RecurrenceDays.Thursday, "Thu"), (RecurrenceDays.Friday, "Fri"), (RecurrenceDays.Saturday, "Sat"),
        (RecurrenceDays.Sunday, "Sun"),
    ];

    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>For example "Every 2 weeks on Mon, Thu" or "Every month on day 15, until 2027-01-31".</summary>
    /// <param name="r">The Recurrence to describe.</param>
    /// <returns>The text.</returns>
    public static string Describe(Recurrence r)
    {
        var unit = r.Frequency switch
        {
            RecurrenceFrequency.Daily => "day",
            RecurrenceFrequency.Weekly => "week",
            RecurrenceFrequency.Monthly => "month",
            _ => "year",
        };
        var text = r.Interval == 1 ? $"Every {unit}" : $"Every {r.Interval} {unit}s";

        text += r.Frequency switch
        {
            RecurrenceFrequency.Weekly =>
                " on " + string.Join(", ", Days.Where(d => r.DaysOfWeek.HasFlag(d.Day)).Select(d => d.Name)),
            RecurrenceFrequency.Monthly => $" on day {r.DayOfMonth}",
            RecurrenceFrequency.Yearly when r.Month is >= 1 and <= 12 => $" on {Months[r.Month.Value - 1]} {r.DayOfMonth}",
            _ => "",
        };

        if (r.Anchor == RecurrenceAnchor.CompletionDate)
        {
            text += ", from completion date";
        }

        if (r.EndDate is { } end)
        {
            text += ", until " + end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return text;
    }
}
