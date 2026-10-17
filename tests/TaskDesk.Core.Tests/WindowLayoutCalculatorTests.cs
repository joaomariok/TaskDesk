using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public sealed class WindowLayoutCalculatorTests
{
    [Theory]
    [InlineData(1000, 1.00, 1000)]
    [InlineData(1000, 1.25, 1250)]
    [InlineData(1000, 1.50, 1500)]
    [InlineData(1000, 2.00, 2000)]
    [InlineData(-300, 1.25, -375)]
    public void Dips_convert_to_physical_pixels_at_each_scale(double dips, double scale, int expected)
    {
        Assert.Equal(expected, WindowLayoutCalculator.ToPhysical(dips, scale));
    }

    [Theory]
    [InlineData(1001, 1.25, 1251)]   // 1251.25 rounds down
    [InlineData(1002, 1.25, 1253)]   // 1252.5 rounds away from zero
    [InlineData(333, 1.50, 500)]     // 499.5 rounds away from zero
    [InlineData(-333, 1.50, -500)]   // -499.5 rounds away from zero
    [InlineData(701, 1.75, 1227)]    // 1226.75 rounds up
    public void Physical_pixels_are_rounded_to_the_nearest_whole_pixel(double dips, double scale, int expected)
    {
        Assert.Equal(expected, WindowLayoutCalculator.ToPhysical(dips, scale));
    }

    [Theory]
    [InlineData(1000, 1.00, 1000)]
    [InlineData(1250, 1.25, 1000)]
    [InlineData(1500, 1.50, 1000)]
    [InlineData(2000, 2.00, 1000)]
    [InlineData(-375, 1.25, -300)]
    [InlineData(1001, 1.25, 800.8)]
    public void Physical_pixels_convert_back_to_dips(int pixels, double scale, double expected)
    {
        Assert.Equal(expected, WindowLayoutCalculator.ToDips(pixels, scale), 6);
    }

    [Theory]
    [InlineData(1.00)]
    [InlineData(1.25)]
    [InlineData(1.50)]
    [InlineData(2.00)]
    public void A_whole_pixel_value_survives_a_round_trip(double scale)
    {
        for (var pixels = -50; pixels <= 3000; pixels++)
        {
            Assert.Equal(pixels, WindowLayoutCalculator.ToPhysical(WindowLayoutCalculator.ToDips(pixels, scale), scale));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void A_scale_that_is_not_positive_is_rejected(double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowLayoutCalculator.ToPhysical(100, scale));
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowLayoutCalculator.ToDips(100, scale));
    }

    [Theory]
    [InlineData(1.00, 100, 50, 1000, 700)]
    [InlineData(1.25, 125, 63, 1250, 875)]
    [InlineData(1.50, 150, 75, 1500, 1050)]
    [InlineData(2.00, 200, 100, 2000, 1400)]
    public void A_layout_converts_to_a_physical_rectangle(
        double scale, int left, int top, int width, int height)
    {
        var rect = WindowLayoutCalculator.ToPhysical(new WindowLayout(100, 50, 1000, 700), scale);

        Assert.Equal(new PixelRect(left, top, width, height), rect);
    }

    // Screen is 0,0 1920x1080; the same cases as WindowLayoutTests, through the calculator.
    [Theory]
    [InlineData(100, 100, 1000, 700, 100, 100, 1000, 700)]
    [InlineData(100, 100, 300, 200, 100, 100, 800, 500)]
    [InlineData(5000, 100, 1000, 700, 920, 100, 1000, 700)]
    [InlineData(-3000, -500, 1000, 700, 0, 0, 1000, 700)]
    [InlineData(0, 0, 4000, 3000, 0, 0, 1920, 1080)]
    public void Fit_keeps_minimum_size_and_stays_on_screen(
        double left, double top, double width, double height,
        double expLeft, double expTop, double expWidth, double expHeight)
    {
        var fitted = WindowLayoutCalculator.Fit(new WindowLayout(left, top, width, height), 0, 0, 1920, 1080);

        Assert.Equal((expLeft, expTop, expWidth, expHeight), (fitted.Left, fitted.Top, fitted.Width, fitted.Height));
    }
}
