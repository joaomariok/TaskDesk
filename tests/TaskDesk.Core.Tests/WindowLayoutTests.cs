using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public sealed class WindowLayoutTests
{
    // Screen is 0,0 1920x1080.
    [Theory]
    [InlineData(100, 100, 1000, 700, 100, 100, 1000, 700)]     // already fits
    [InlineData(100, 100, 300, 200, 100, 100, 800, 500)]       // below minimum size
    [InlineData(5000, 100, 1000, 700, 920, 100, 1000, 700)]    // off-screen right
    [InlineData(-3000, -500, 1000, 700, 0, 0, 1000, 700)]     // off-screen left/top
    [InlineData(0, 0, 4000, 3000, 0, 0, 1920, 1080)]           // larger than screen
    public void FitTo_keeps_minimum_size_and_stays_on_screen(
        double left, double top, double width, double height,
        double expLeft, double expTop, double expWidth, double expHeight)
    {
        var fitted = new WindowLayout(left, top, width, height).FitTo(0, 0, 1920, 1080);

        Assert.Equal((expLeft, expTop, expWidth, expHeight), (fitted.Left, fitted.Top, fitted.Width, fitted.Height));
    }

    [Fact]
    public void FitTo_respects_a_screen_that_does_not_start_at_the_origin()
    {
        var fitted = new WindowLayout(0, 0, 1000, 700).FitTo(-1920, 0, 1920, 1080);

        Assert.Equal(-1000, fitted.Left);
    }
}
