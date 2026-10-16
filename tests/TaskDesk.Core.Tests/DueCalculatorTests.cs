using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public class DueCalculatorTests
{
    // A fixed zone, 3 hours behind UTC and without daylight saving, so the tests do not depend on the machine.
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test-3", TimeSpan.FromHours(-3), "Test-3", "Test-3");

    private static readonly DateOnly Day = new(2026, 10, 20);

    // The clock is set to a local wall-clock time in the fixed zone.
    private static FakeTimeProvider ClockAt(int year, int month, int day, int hour, int minute, int second = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        var clock = new FakeTimeProvider(new DateTimeOffset(local, Zone.GetUtcOffset(local)));
        clock.SetLocalTimeZone(Zone);
        return clock;
    }

    private static bool Overdue(DateOnly? date, TimeOnly? time, bool completed, FakeTimeProvider clock) =>
        DueCalculator.IsOverdue(date, time, completed, clock.GetUtcNow(), clock.LocalTimeZone);

    public static TheoryData<string, int, int, int, int, int, int, bool> OverdueCases => new()
    {
        // description, now (day of October 2026, hour, minute, second), Due time (hour, minute; -1 = date-only), expected
        { "date-only, same day just before midnight", 20, 23, 59, 59, -1, -1, false },
        { "date-only, same day at noon", 20, 12, 0, 0, -1, -1, false },
        { "date-only, at the start of the next day", 21, 0, 0, 0, -1, -1, true },
        { "date-only, a day later", 22, 8, 0, 0, -1, -1, true },
        { "date-only, the day before", 19, 23, 59, 59, -1, -1, false },
        { "timed, a second before the time", 20, 13, 59, 59, 14, 0, false },
        { "timed, exactly at the time", 20, 14, 0, 0, 14, 0, false },
        { "timed, a second after the time", 20, 14, 0, 1, 14, 0, true },
        { "timed, a minute after the time", 20, 14, 1, 0, 14, 0, true },
        { "timed at midnight, exactly at midnight", 20, 0, 0, 0, 0, 0, false },
        { "timed at midnight, one second later", 20, 0, 0, 1, 0, 0, true },
        { "timed late evening, still before it", 20, 23, 0, 0, 23, 30, false },
        { "timed, the next day even before its time of day", 21, 0, 0, 0, 23, 59, true },
        { "timed, the day before", 19, 23, 59, 59, 0, 0, false },
    };

    [Theory]
    [MemberData(nameof(OverdueCases))]
    public void Overdue_boundaries(
        string description, int day, int hour, int minute, int second, int dueHour, int dueMinute,
        bool expected)
    {
        _ = description;
        var clock = ClockAt(2026, 10, day, hour, minute, second);
        TimeOnly? dueTime = dueHour < 0 ? null : new TimeOnly(dueHour, dueMinute);

        Assert.Equal(expected, Overdue(Day, dueTime, completed: false, clock));
    }

    [Fact]
    public void A_completed_Task_is_never_overdue()
    {
        var clock = ClockAt(2026, 10, 25, 10, 0);

        Assert.False(Overdue(Day, new TimeOnly(9, 0), completed: true, clock));
        Assert.False(Overdue(Day, null, completed: true, clock));
    }

    [Fact]
    public void A_Task_without_a_Due_date_is_never_overdue()
    {
        var clock = ClockAt(2026, 10, 25, 10, 0);

        Assert.False(Overdue(null, null, completed: false, clock));
    }

    [Theory]
    [InlineData(2, 59, 59, false)] // UTC 02:59:59 on the 21st is 23:59:59 on the 20th locally
    [InlineData(3, 0, 0, true)] // UTC 03:00 on the 21st is local midnight
    public void The_local_day_decides_not_the_UTC_day(int utcHour, int utcMinute, int utcSecond, bool expected)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, utcHour, utcMinute, utcSecond, TimeSpan.Zero));
        clock.SetLocalTimeZone(Zone);

        Assert.Equal(expected, Overdue(Day, null, completed: false, clock));
    }

    [Fact]
    public void A_timed_Due_compares_against_local_time_not_UTC()
    {
        // 16:59 UTC is 13:59 locally: a 14:00 Due has not passed; 17:01 UTC is 14:01 locally: it has.
        var before = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 16, 59, 0, TimeSpan.Zero));
        var after = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 17, 1, 0, TimeSpan.Zero));
        before.SetLocalTimeZone(Zone);
        after.SetLocalTimeZone(Zone);

        Assert.False(Overdue(Day, new TimeOnly(14, 0), completed: false, before));
        Assert.True(Overdue(Day, new TimeOnly(14, 0), completed: false, after));
    }

    [Theory]
    [InlineData(19, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void Today_view_takes_Due_today_or_earlier(int dueDay, bool expected)
    {
        var clock = ClockAt(2026, 10, 20, 9, 0);

        Assert.Equal(
            expected,
            DueCalculator.IsInToday(new DateOnly(2026, 10, dueDay), false, clock.GetUtcNow(), clock.LocalTimeZone));
    }

    [Fact]
    public void Today_view_excludes_completed_and_undated_Tasks()
    {
        var clock = ClockAt(2026, 10, 20, 9, 0);

        Assert.False(DueCalculator.IsInToday(Day, true, clock.GetUtcNow(), clock.LocalTimeZone));
        Assert.False(DueCalculator.IsInToday(null, false, clock.GetUtcNow(), clock.LocalTimeZone));
    }

    [Theory]
    [InlineData(20, false)] // today is not Upcoming
    [InlineData(21, true)] // tomorrow
    [InlineData(27, true)] // today + 7 is included
    [InlineData(28, false)] // today + 8
    [InlineData(19, false)]
    public void Upcoming_view_is_the_range_after_today_up_to_seven_days(int dueDay, bool expected)
    {
        var clock = ClockAt(2026, 10, 20, 23, 59, 59);

        Assert.Equal(
            expected,
            DueCalculator.IsUpcoming(new DateOnly(2026, 10, dueDay), false, clock.GetUtcNow(), clock.LocalTimeZone));
    }

    [Fact]
    public void Upcoming_view_excludes_completed_and_undated_Tasks()
    {
        var clock = ClockAt(2026, 10, 20, 9, 0);
        var tomorrow = new DateOnly(2026, 10, 21);

        Assert.False(DueCalculator.IsUpcoming(tomorrow, true, clock.GetUtcNow(), clock.LocalTimeZone));
        Assert.False(DueCalculator.IsUpcoming(null, false, clock.GetUtcNow(), clock.LocalTimeZone));
    }

    [Fact]
    public void Upcoming_range_moves_at_local_midnight()
    {
        var justBefore = ClockAt(2026, 10, 20, 23, 59, 59);
        var justAfter = ClockAt(2026, 10, 21, 0, 0, 0);
        var due = new DateOnly(2026, 10, 28);

        Assert.False(DueCalculator.IsUpcoming(due, false, justBefore.GetUtcNow(), Zone));
        Assert.True(DueCalculator.IsUpcoming(due, false, justAfter.GetUtcNow(), Zone));
    }

    [Fact]
    public void A_Due_time_without_a_Due_date_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => DueCalculator.ValidateDue(null, new TimeOnly(9, 0)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Valid_Due_combinations_are_accepted(bool hasDate, bool hasTime)
    {
        DueCalculator.ValidateDue(
            hasDate ? Day : null,
            hasTime ? new TimeOnly(9, 0) : null);
    }
}
