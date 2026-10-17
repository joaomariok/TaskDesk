using TaskDesk.Core.Export;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public sealed class RecurrenceTextTests
{
    public static TheoryData<Recurrence, string> Cases => new()
    {
        { Recurrence.Daily(), "Every day" },
        { Recurrence.Daily(3), "Every 3 days" },
        { Recurrence.Weekdays(), "Every week on Mon, Tue, Wed, Thu, Fri" },
        { Recurrence.Weekly(RecurrenceDays.Monday | RecurrenceDays.Thursday, 2), "Every 2 weeks on Mon, Thu" },
        { Recurrence.Weekly(RecurrenceDays.Sunday), "Every week on Sun" },
        { Recurrence.Monthly(15), "Every month on day 15" },
        { Recurrence.Monthly(31, 3), "Every 3 months on day 31" },
        { Recurrence.Yearly(3, 14), "Every year on Mar 14" },
        { Recurrence.Yearly(2, 29, 4), "Every 4 years on Feb 29" },
        { Recurrence.Daily() with { Anchor = RecurrenceAnchor.CompletionDate }, "Every day, from completion date" },
        { Recurrence.Monthly(1) with { EndDate = new DateOnly(2027, 1, 31) }, "Every month on day 1, until 2027-01-31" },
        {
            Recurrence.Weekly(RecurrenceDays.Friday) with
            {
                Anchor = RecurrenceAnchor.CompletionDate, EndDate = new DateOnly(2027, 12, 31),
            },
            "Every week on Fri, from completion date, until 2027-12-31"
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Describes_the_rule_in_plain_text(Recurrence recurrence, string expected)
    {
        Assert.Equal(expected, RecurrenceText.Describe(recurrence));
    }
}
