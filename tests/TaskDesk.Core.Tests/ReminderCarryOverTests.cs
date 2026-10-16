using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Reminders;

namespace TaskDesk.Core.Tests;

public class ReminderCarryOverTests
{
    // 3 hours behind UTC, without daylight saving.
    private static readonly TimeZoneInfo FixedZone =
        TimeZoneInfo.CreateCustomTimeZone("Test-3", TimeSpan.FromHours(-3), "Test-3", "Test-3");

    // UTC+1 with daylight saving (UTC+2) from the last Sunday of March 02:00 to the last Sunday of October 03:00.
    // In 2026 that is 29 March and 25 October.
    private static readonly TimeZoneInfo DaylightZone = TimeZoneInfo.CreateCustomTimeZone(
        "Test+1-DST", TimeSpan.FromHours(1), "Test+1-DST", "Test+1", "Test+2",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday)),
        ]);

    public static TheoryData<string, string, string, string, string, string, string> Cases => new()
    {
        // description, zone, Due date, Due time ("" = date-only), planned Reminder (UTC), next Due date,
        // expected next Reminder (UTC)
        { "timed Due, an hour before", "fixed", "2026-10-20", "14:00", "2026-10-20T16:00", "2026-10-27", "2026-10-27T16:00" },
        { "timed Due, after the Due", "fixed", "2026-10-20", "10:00", "2026-10-20T14:00", "2026-11-03", "2026-11-03T14:00" },
        { "date-only Due, 09:00 local is 9 h after local midnight", "fixed", "2026-10-20", "", "2026-10-20T12:00", "2026-10-21", "2026-10-21T12:00" },
        { "date-only Due, the evening before", "fixed", "2026-10-20", "", "2026-10-19T23:00", "2026-10-22", "2026-10-21T23:00" },
        { "date-only Due, local midnight is not UTC midnight", "fixed", "2026-10-20", "", "2026-10-20T02:00", "2026-10-21", "2026-10-21T02:00" },
        { "date-only Due across the end of daylight saving keeps 09:00 local", "dst", "2026-10-24", "", "2026-10-24T07:00", "2026-10-25", "2026-10-25T08:00" },
        { "date-only Due across the start of daylight saving keeps 09:00 local", "dst", "2026-03-28", "", "2026-03-28T08:00", "2026-03-29", "2026-03-29T07:00" },
        { "a Reminder that lands in the skipped hour moves forward by the gap", "dst", "2026-03-22", "03:00", "2026-03-22T01:30", "2026-03-29", "2026-03-29T01:30" },
        { "a Reminder that lands in the repeated hour takes its first occurrence", "dst", "2026-10-18", "03:00", "2026-10-18T00:30", "2026-10-25", "2026-10-25T00:30" },
        { "timed Due a month later, in standard time", "dst", "2026-10-20", "18:00", "2026-10-20T15:00", "2026-11-20", "2026-11-20T16:00" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_next_Occurrence_keeps_the_planned_Reminder_offset_from_the_Due_in_local_time(
        string description, string zone, string dueDate, string dueTime, string planned, string nextDueDate,
        string expected)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(zone == "dst" ? DaylightZone : FixedZone);

        var next = ReminderCalculator.CarryOver(
            Utc(planned), DateOnly.Parse(dueDate, CultureInfo.InvariantCulture),
            dueTime.Length == 0 ? null : TimeOnly.Parse(dueTime, CultureInfo.InvariantCulture),
            DateOnly.Parse(nextDueDate, CultureInfo.InvariantCulture), clock.LocalTimeZone);

        Assert.True(Utc(expected) == next, $"{description}: expected {expected}, got {next:O}");
        Assert.Equal(DateTimeKind.Utc, next.Kind);
    }

    private static DateTime Utc(string text) => DateTime.SpecifyKind(
        DateTime.ParseExact(text, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture), DateTimeKind.Utc);
}
