using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

/// <summary>
/// The saved layout on a mixed-DPI desk: each monitor converts at its own scale, so a window saves and
/// restores on the monitor it was on, at the same physical size.
/// </summary>
public sealed class MonitorLayoutTests
{
    // Primary 1920x1080 at 100 % (taskbar 40 px); secondary 2880x1620 at 150 % to its right (taskbar 60 px).
    private static readonly MonitorArea Primary =
        new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1.0);

    private static readonly MonitorArea Secondary =
        new(new PixelRect(1920, 0, 2880, 1620), new PixelRect(1920, 0, 2880, 1560), 1.5);

    // A 150 % monitor left of the primary one.
    private static readonly MonitorArea LeftOfPrimary =
        new(new PixelRect(-2880, 0, 2880, 1620), new PixelRect(-2880, 0, 2880, 1560), 1.5);

    private static readonly MonitorArea[] Desk = [Primary, Secondary];

    public static TheoryData<string, int, int, int, int> WindowsOnTheDesk => new()
    {
        { "primary", 100, 50, 1000, 700 },
        { "secondary", 2000, 150, 1500, 1050 },
        { "secondary, far corner", 3200, 450, 1500, 1050 },
        { "left of primary", -2000, 150, 1200, 900 },
    };

    [Theory]
    [MemberData(nameof(WindowsOnTheDesk))]
    public void A_window_restores_where_it_was_saved(string monitor, int left, int top, int width, int height)
    {
        MonitorArea[] desk = [Primary, Secondary, LeftOfPrimary];
        var bounds = new PixelRect(left, top, width, height);

        var restored = WindowLayoutCalculator.Place(WindowLayoutCalculator.ToLayout(bounds, false, desk), desk);

        Assert.True(bounds == restored, $"{monitor}: expected {bounds}, got {restored}");
    }

    [Fact]
    public void A_window_on_the_150_percent_monitor_saves_its_size_at_that_scale()
    {
        var layout = WindowLayoutCalculator.ToLayout(new PixelRect(2000, 150, 1500, 1050), true, Desk);

        // Positions are measured from the monitor's own origin: 1920 + 80 / 1.5, 150 / 1.5.
        Assert.Equal(1920 + (80 / 1.5), layout.Left, 6);
        Assert.Equal(100, layout.Top, 6);
        Assert.Equal((1000.0, 700.0, true), (layout.Width, layout.Height, layout.Maximized));
    }

    [Fact]
    public void A_layout_on_a_single_scaled_monitor_matches_the_plain_conversion()
    {
        MonitorArea[] desk = [new(new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1390), 1.25)];

        var layout = WindowLayoutCalculator.ToLayout(new PixelRect(125, 250, 1250, 875), false, desk);

        Assert.Equal((100.0, 200.0, 1000.0, 700.0), (layout.Left, layout.Top, layout.Width, layout.Height));
    }

    [Fact]
    public void A_layout_saved_on_the_secondary_monitor_restores_there_at_its_scale()
    {
        var placed = WindowLayoutCalculator.Place(new WindowLayout(2000, 100, 1000, 700), Desk);

        Assert.Equal(new PixelRect(2040, 150, 1500, 1050), placed);
    }

    [Fact]
    public void A_layout_from_a_monitor_that_is_gone_moves_onto_the_nearest_work_area()
    {
        var placed = WindowLayoutCalculator.Place(new WindowLayout(2000, 100, 1000, 700), [Primary]);

        Assert.Equal(new PixelRect(920, 100, 1000, 700), placed);
    }

    [Fact]
    public void A_layout_taller_than_the_work_area_is_cut_to_it_above_the_taskbar()
    {
        var placed = WindowLayoutCalculator.Place(new WindowLayout(100, 300, 1000, 1200), Desk);

        Assert.Equal(new PixelRect(100, 0, 1000, 1040), placed);
    }

    [Fact]
    public void A_layout_below_the_minimum_size_grows_to_it()
    {
        var placed = WindowLayoutCalculator.Place(new WindowLayout(2000, 100, 300, 200), Desk);

        Assert.Equal(new PixelRect(2040, 150, 1200, 750), placed);
    }

    [Fact]
    public void A_window_straddling_two_monitors_saves_on_the_one_holding_most_of_it()
    {
        // 120 px on the primary, 1380 px on the secondary.
        var layout = WindowLayoutCalculator.ToLayout(new PixelRect(1800, 0, 1500, 1050), false, Desk);

        Assert.Equal((1920 - (120 / 1.5), 1000.0), (layout.Left, layout.Width));
    }

    [Fact]
    public void No_monitor_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => WindowLayoutCalculator.Place(new WindowLayout(0, 0, 800, 500), []));
        Assert.Throws<ArgumentException>(
            () => WindowLayoutCalculator.ToLayout(new PixelRect(0, 0, 800, 500), false, []));
    }
}
