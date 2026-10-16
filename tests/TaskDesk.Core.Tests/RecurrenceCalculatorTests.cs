using System.Globalization;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public class RecurrenceCalculatorTests
{
    private static DateOnly D(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Rules are written compactly: "daily/N", "weekly/N/Mon,Fri", "monthly/N/day", "yearly/N/month/day".
    private static Recurrence Parse(string rule, string anchor, string end)
    {
        var parts = rule.Split('/');
        var interval = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var recurrence = parts[0] switch
        {
            "daily" => Recurrence.Daily(interval),
            "weekly" => Recurrence.Weekly(
                parts[2].Split(',').Select(Enum.Parse<DayOfWeek>).Aggregate(
                    RecurrenceDays.None, (days, day) => days | day.ToRecurrenceDay()),
                interval),
            "monthly" => Recurrence.Monthly(int.Parse(parts[2], CultureInfo.InvariantCulture), interval),
            "yearly" => Recurrence.Yearly(
                int.Parse(parts[2], CultureInfo.InvariantCulture), int.Parse(parts[3], CultureInfo.InvariantCulture),
                interval),
            _ => throw new ArgumentException(rule),
        };
        return recurrence with
        {
            Anchor = anchor == "done" ? RecurrenceAnchor.CompletionDate : RecurrenceAnchor.DueDate,
            EndDate = end.Length == 0 ? null : D(end),
        };
    }

    // description, rule, anchor ("due" or "done"), end date ("" = none), current Due, completion date, expected ("" = none)
    public static TheoryData<string, string, string, string, string, string, string> Cases => new()
    {
        // Daily
        { "daily", "daily/1", "due", "", "2026-10-20", "2026-10-20", "2026-10-21" },
        { "daily ignores the completion date with a Due anchor", "daily/1", "due", "", "2026-10-20", "2026-10-25", "2026-10-21" },
        { "every 3 days", "daily/3", "due", "", "2026-10-20", "2026-10-20", "2026-10-23" },
        { "daily across a month end", "daily/1", "due", "", "2026-10-31", "2026-10-31", "2026-11-01" },
        { "daily across a year end", "daily/2", "due", "", "2026-12-31", "2026-12-31", "2027-01-02" },
        { "daily from the completion date, completed late", "daily/1", "done", "", "2026-10-20", "2026-10-25", "2026-10-26" },
        { "daily from the completion date, completed on the Due", "daily/1", "done", "", "2026-10-20", "2026-10-20", "2026-10-21" },
        { "daily from the completion date, completed early: strictly after the Due", "daily/1", "done", "", "2026-10-20", "2026-10-18", "2026-10-21" },
        { "every 3 days, completed a day early: next step is already after the Due", "daily/3", "done", "", "2026-10-20", "2026-10-19", "2026-10-22" },

        // Weekly (weeks start on Monday). 2026-10-19 is a Monday.
        { "weekly on the same weekday", "weekly/1/Tuesday", "due", "", "2026-10-20", "2026-10-20", "2026-10-27" },
        { "weekly, the next selected day in the same week", "weekly/1/Monday,Wednesday,Friday", "due", "", "2026-10-19", "2026-10-19", "2026-10-21" },
        { "weekly, Friday wraps to Monday of the next week", "weekly/1/Monday,Wednesday,Friday", "due", "", "2026-10-23", "2026-10-23", "2026-10-26" },
        { "weekdays, Friday to Monday", "weekly/1/Monday,Tuesday,Wednesday,Thursday,Friday", "due", "", "2026-10-23", "2026-10-23", "2026-10-26" },
        { "weekdays, Monday to Tuesday", "weekly/1/Monday,Tuesday,Wednesday,Thursday,Friday", "due", "", "2026-10-19", "2026-10-19", "2026-10-20" },
        { "weekly, Sunday is the last day of the week", "weekly/1/Monday,Sunday", "due", "", "2026-10-19", "2026-10-19", "2026-10-25" },
        { "weekly, Sunday wraps to Monday", "weekly/1/Monday,Sunday", "due", "", "2026-10-25", "2026-10-25", "2026-10-26" },
        { "every 2 weeks, remaining day in the same week first", "weekly/2/Monday,Thursday", "due", "", "2026-10-19", "2026-10-19", "2026-10-22" },
        { "every 2 weeks, after the last selected day jump 2 weeks", "weekly/2/Monday,Thursday", "due", "", "2026-10-22", "2026-10-22", "2026-11-02" },
        { "every 2 weeks on Sunday", "weekly/2/Sunday", "due", "", "2026-10-25", "2026-10-25", "2026-11-08" },
        { "every 3 weeks on one day", "weekly/3/Wednesday", "due", "", "2026-10-21", "2026-10-21", "2026-11-11" },
        { "weekly across a year end", "weekly/1/Thursday", "due", "", "2026-12-31", "2026-12-31", "2027-01-07" },
        { "weekly from the completion date", "weekly/1/Monday", "done", "", "2026-10-19", "2026-10-21", "2026-10-26" },
        { "weekly from the completion date, completed early: strictly after the Due", "weekly/1/Monday,Wednesday", "done", "", "2026-10-21", "2026-10-19", "2026-10-26" },

        // Monthly
        { "monthly on the 15th", "monthly/1/15", "due", "", "2026-10-15", "2026-10-15", "2026-11-15" },
        { "monthly on the 31st clamps to April 30", "monthly/1/31", "due", "", "2026-03-31", "2026-03-31", "2026-04-30" },
        { "monthly on the 31st returns to 31 after a short month", "monthly/1/31", "due", "", "2026-04-30", "2026-04-30", "2026-05-31" },
        { "monthly on the 31st clamps to Feb 28", "monthly/1/31", "due", "", "2026-01-31", "2026-01-31", "2026-02-28" },
        { "monthly on the 30th clamps to Feb 29 in a leap year", "monthly/1/30", "due", "", "2028-01-30", "2028-01-30", "2028-02-29" },
        { "monthly on the 31st, from Feb 28 back to Mar 31", "monthly/1/31", "due", "", "2026-02-28", "2026-02-28", "2026-03-31" },
        { "every 3 months", "monthly/3/10", "due", "", "2026-11-10", "2026-11-10", "2027-02-10" },
        { "every 12 months", "monthly/12/5", "due", "", "2026-10-05", "2026-10-05", "2027-10-05" },
        { "monthly across a year end", "monthly/1/20", "due", "", "2026-12-20", "2026-12-20", "2027-01-20" },
        { "monthly from the completion date", "monthly/1/10", "done", "", "2026-10-10", "2026-11-02", "2026-12-10" },
        { "monthly from the completion date, completed early in the month before", "monthly/1/10", "done", "", "2026-10-10", "2026-09-28", "2026-11-10" },

        // Yearly (Month and DayOfMonth are stored, never inferred from the previous Due)
        { "yearly", "yearly/1/10/20", "due", "", "2026-10-20", "2026-10-20", "2027-10-20" },
        { "every 2 years", "yearly/2/3/15", "due", "", "2026-03-15", "2026-03-15", "2028-03-15" },
        { "yearly Feb 29 in a non-leap year is Feb 28", "yearly/1/2/29", "due", "", "2028-02-29", "2028-02-29", "2029-02-28" },
        { "yearly Feb 29 does not drift to Feb 28", "yearly/1/2/29", "due", "", "2030-02-28", "2030-02-28", "2031-02-28" },
        { "yearly Feb 29 returns to Feb 29 in a leap year", "yearly/1/2/29", "due", "", "2031-02-28", "2031-02-28", "2032-02-29" },
        { "every 4 years on Feb 29", "yearly/4/2/29", "due", "", "2028-02-29", "2028-02-29", "2032-02-29" },
        { "yearly on Dec 31", "yearly/1/12/31", "due", "", "2026-12-31", "2026-12-31", "2027-12-31" },
        { "yearly from the completion date", "yearly/1/10/20", "done", "", "2026-10-20", "2027-01-05", "2028-10-20" },
        { "yearly from the completion date, completed early", "yearly/1/10/20", "done", "", "2026-10-20", "2026-10-01", "2027-10-20" },

        // End date
        { "the next date on the end date is kept", "daily/1", "due", "2026-10-21", "2026-10-20", "2026-10-20", "2026-10-21" },
        { "the next date after the end date ends the Series", "daily/1", "due", "2026-10-20", "2026-10-20", "2026-10-20", "" },
        { "weekly past the end date", "weekly/1/Monday", "due", "2026-10-25", "2026-10-19", "2026-10-19", "" },
        { "monthly before the end date", "monthly/1/31", "due", "2026-12-31", "2026-11-30", "2026-11-30", "2026-12-31" },
        { "yearly past the end date", "yearly/1/2/29", "due", "2029-02-27", "2028-02-29", "2028-02-29", "" },
        { "completion-date anchor past the end date", "daily/1", "done", "2026-10-25", "2026-10-20", "2026-10-25", "" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Next_Due_date(
        string description, string rule, string anchor, string end, string currentDue, string completion,
        string expected)
    {
        _ = description;
        var recurrence = Parse(rule, anchor, end);

        var next = RecurrenceCalculator.GetNextDueDate(recurrence, D(currentDue), D(completion));

        Assert.Equal(expected.Length == 0 ? null : D(expected), next);
    }

    public static TheoryData<string> InvalidRules => new()
    {
        "interval 0",
        "weekly with no days",
        "monthly day 0",
        "monthly day 32",
        "yearly month 13",
        "yearly Feb 30",
        "yearly Apr 31",
        "daily with days",
        "daily with a day of month",
    };

    [Theory]
    [MemberData(nameof(InvalidRules))]
    public void Invalid_Recurrences_are_rejected(string rule)
    {
        Recurrence recurrence = rule switch
        {
            "interval 0" => Recurrence.Daily() with { Interval = 0 },
            "weekly with no days" => Recurrence.Weekly(RecurrenceDays.Monday) with { DaysOfWeek = RecurrenceDays.None },
            "monthly day 0" => Recurrence.Monthly(1) with { DayOfMonth = 0 },
            "monthly day 32" => Recurrence.Monthly(1) with { DayOfMonth = 32 },
            "yearly month 13" => Recurrence.Yearly(1, 1) with { Month = 13 },
            "yearly Feb 30" => Recurrence.Yearly(2, 1) with { DayOfMonth = 30 },
            "yearly Apr 31" => Recurrence.Yearly(4, 1) with { DayOfMonth = 31 },
            "daily with days" => Recurrence.Daily() with { DaysOfWeek = RecurrenceDays.Monday },
            _ => Recurrence.Daily() with { DayOfMonth = 3 },
        };

        Assert.Throws<ArgumentException>(recurrence.Validate);
        Assert.Throws<ArgumentException>(
            () => RecurrenceCalculator.GetNextDueDate(recurrence, D("2026-10-20"), D("2026-10-20")));
    }

    [Fact]
    public void Yearly_Feb_29_is_valid()
    {
        Recurrence.Yearly(2, 29).Validate();
    }

    [Fact]
    public void The_Weekdays_preset_is_Monday_to_Friday_every_week()
    {
        var weekdays = Recurrence.Weekdays();

        Assert.Equal(RecurrenceFrequency.Weekly, weekdays.Frequency);
        Assert.Equal(1, weekdays.Interval);
        Assert.Equal(
            RecurrenceDays.Monday | RecurrenceDays.Tuesday | RecurrenceDays.Wednesday | RecurrenceDays.Thursday
            | RecurrenceDays.Friday,
            weekdays.DaysOfWeek);
    }

    [Fact]
    public void Presets_default_to_the_Due_date_anchor_and_no_end()
    {
        var daily = Recurrence.Daily();

        Assert.Equal(RecurrenceAnchor.DueDate, daily.Anchor);
        Assert.Null(daily.EndDate);
    }
}
