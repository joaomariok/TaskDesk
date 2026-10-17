using System.Globalization;

namespace TaskDesk.Presentation;

/// <summary>How a row's Due reads: drives the colour of the Due chip.</summary>
public enum DueState
{
    /// <summary>The Task has no Due; no chip shows.</summary>
    None,

    /// <summary>The Task is Overdue.</summary>
    Overdue,

    /// <summary>The Task is Due today and not Overdue (yet).</summary>
    Today,

    /// <summary>Any other Due: later, or earlier on a Task that is not Overdue (completed).</summary>
    Later,
}

/// <summary>Relative wording for a Due: "Today", "Tomorrow", "Thu", "Thu 15 Oct 2027", plus " HH:mm" when timed.</summary>
internal static class DueDisplay
{
    /// <summary>Within this many days either side of today a Due reads as a weekday.</summary>
    private const int WeekdayDays = 6;

    public static string Format(DateOnly date, TimeOnly? time, DateOnly today, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var offset = date.DayNumber - today.DayNumber;
        var text = offset switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            >= -WeekdayDays and <= WeekdayDays => date.ToString("ddd", culture),
            _ => date.ToString(date.Year == today.Year ? "ddd d MMM" : "ddd d MMM yyyy", culture),
        };
        return time is { } t ? $"{text} {t.ToString("HH:mm", culture)}" : text;
    }
}
