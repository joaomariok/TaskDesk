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

        var restored = WindowLayoutCalculator.Place(WindowLayoutCalculator.ToLayout(bounds, false, desk, 1.0), desk, 1.0);

        Assert.True(bounds == restored, $"{monitor}: expected {bounds}, got {restored}");
    }

    [Fact]
    public void A_window_on_the_150_percent_monitor_saves_its_size_at_that_scale()
    {
        var layout = WindowLayoutCalculator.ToLayout(new PixelRect(2000, 150, 1500, 1050), true, Desk, 1.0);

        // Positions are measured from the monitor's own origin: 1920 + 80 / 1.5, 150 / 1.5.
        Assert.Equal(1920 + (80 / 1.5), layout.Left, 6);
        Assert.Equal(100, layout.Top, 6);
        Assert.Equal((1000.0, 700.0, true), (layout.Width, layout.Height, layout.Maximized));
    }

    [Fact]
    public void A_layout_on_a_single_scaled_monitor_matches_the_plain_conversion()
    {
        MonitorArea[] desk = [new(new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1390), 1.25)];

        var layout = WindowLayoutCalculator.ToLayout(new PixelRect(125, 250, 1250, 875), false, desk, 1.25);

        Assert.Equal((100.0, 200.0, 1000.0, 700.0), (layout.Left, layout.Top, layout.Width, layout.Height));
    }

    [Fact]
    public void A_layout_saved_on_the_secondary_monitor_restores_there_at_its_scale()
    {
        var placed = WindowLayoutCalculator.Place(new WindowLayout(2000, 100, 1000, 700), Desk, 1.0);

        Assert.Equal(new PixelRect(2040, 150, 1500, 1050), placed);
    }

    [Fact]
    public void A_layout_from_a_monitor_that_is_gone_moves_onto_the_nearest_work_area()
    {
        var placed = WindowLayoutCalculator.Place(new WindowLayout(2000, 100, 1000, 700), [Primary], 1.0);

        Assert.Equal(new PixelRect(920, 100, 1000, 700), placed);
    }

    [Fact]
    public void A_layout_taller_than_the_work_area_is_cut_to_it_above_the_taskbar()
    {
        var placed = WindowLayoutCalculator.Place(new WindowLayout(100, 300, 1000, 1200), Desk, 1.0);

        Assert.Equal(new PixelRect(100, 0, 1000, 1040), placed);
    }

    [Fact]
    public void A_layout_below_the_minimum_size_grows_to_it()
    {
        var placed = WindowLayoutCalculator.Place(new WindowLayout(2000, 100, 300, 200), Desk, 1.0);

        Assert.Equal(new PixelRect(2040, 150, 1200, 750), placed);
    }

    [Fact]
    public void A_window_straddling_two_monitors_saves_on_the_one_holding_most_of_it()
    {
        // 120 px on the primary, 1380 px on the secondary.
        var layout = WindowLayoutCalculator.ToLayout(new PixelRect(1800, 0, 1500, 1050), false, Desk, 1.0);

        Assert.Equal((1920 - (120 / 1.5), 1000.0), (layout.Left, layout.Width));
    }

    // The WPF app is system-DPI aware (no manifest; WPF opts the process in): its Left/Top/Width/Height are the
    // coordinates Windows virtualizes for it, divided by the system scale. Windows virtualizes a monitor at another
    // scale about its own top-left corner, so a physical point p on a monitor with origin O and scale s is stored as
    // O / S + (p - O) / s, where S is the system scale (the primary monitor's at sign-in).
    // This desk: primary 2880x1620 at 150 % (so S = 1.5), taskbar 60 px; a 100 % monitor right of it and one left.
    private static readonly MonitorArea ScaledPrimary =
        new(new PixelRect(0, 0, 2880, 1620), new PixelRect(0, 0, 2880, 1560), 1.5);

    private static readonly MonitorArea RightAt100 =
        new(new PixelRect(2880, 0, 1920, 1080), new PixelRect(2880, 0, 1920, 1040), 1.0);

    private static readonly MonitorArea LeftAt100 =
        new(new PixelRect(-1920, 0, 1920, 1080), new PixelRect(-1920, 0, 1920, 1040), 1.0);

    private static readonly MonitorArea[] ScaledDesk = [ScaledPrimary, RightAt100, LeftAt100];

    private const double SystemScale = 1.5;

    // Each window in physical pixels, and the numbers the WPF app writes for it, worked out by hand.
    public static TheoryData<string, PixelRect, double, double, double, double> AsTheWpfAppWritesThem => new()
    {
        // 150 / 1.5, 300 / 1.5, 1800 / 1.5, 1200 / 1.5.
        { "primary at 150 %", new PixelRect(150, 300, 1800, 1200), 100, 200, 1200, 800 },
        // 2880 / 1.5 + (3000 - 2880), 100, 1200, 800.
        { "100 % monitor right of it", new PixelRect(3000, 100, 1200, 800), 2040, 100, 1200, 800 },
        // -1920 / 1.5 + (-1800 + 1920), 60, 1200, 900.
        { "100 % monitor left of it", new PixelRect(-1800, 60, 1200, 900), -1160, 60, 1200, 900 },
    };

    [Theory]
    [MemberData(nameof(AsTheWpfAppWritesThem))]
    public void A_window_saves_the_numbers_the_WPF_app_writes(
        string monitor, PixelRect bounds, double left, double top, double width, double height)
    {
        var layout = WindowLayoutCalculator.ToLayout(bounds, false, ScaledDesk, SystemScale);

        Assert.True(
            (left, top, width, height) == (layout.Left, layout.Top, layout.Width, layout.Height),
            $"{monitor}: expected {(left, top, width, height)}, got {(layout.Left, layout.Top, layout.Width, layout.Height)}");
    }

    [Theory]
    [MemberData(nameof(AsTheWpfAppWritesThem))]
    public void A_layout_the_WPF_app_wrote_restores_on_its_monitor_at_its_size(
        string monitor, PixelRect bounds, double left, double top, double width, double height)
    {
        var placed = WindowLayoutCalculator.Place(new WindowLayout(left, top, width, height), ScaledDesk, SystemScale);

        Assert.True(bounds == placed, $"{monitor}: expected {bounds}, got {placed}");
    }

    [Fact]
    public void A_WPF_layout_past_a_100_percent_monitors_work_area_is_fitted_to_it()
    {
        // WPF's DIPs for the right monitor's work area: 1920 to 3840 across, 0 to 1040 down.
        var placed = WindowLayoutCalculator.Place(new WindowLayout(3500, 600, 1000, 700), ScaledDesk, SystemScale);

        Assert.Equal(new PixelRect(3800, 340, 1000, 700), placed);
    }

    [Fact]
    public void No_monitor_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => WindowLayoutCalculator.Place(new WindowLayout(0, 0, 800, 500), [], 1.0));
        Assert.Throws<ArgumentException>(
            () => WindowLayoutCalculator.ToLayout(new PixelRect(0, 0, 800, 500), false, [], 1.0));
    }
}
