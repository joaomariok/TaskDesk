using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public sealed class SettingsPlacementTests
{
    [Theory]
    // Work area left, top, width, height; window size in DIPs; scale; expected physical rectangle.
    [InlineData(0, 0, 1920, 1040, 640, 720, 1.00, 640, 160, 640, 720)]
    [InlineData(0, 0, 2560, 1400, 640, 720, 1.25, 880, 250, 800, 900)]
    // A monitor left of the primary one, with the taskbar on top (work area starts below it).
    [InlineData(-1920, 48, 1920, 1032, 640, 720, 1.00, -1280, 204, 640, 720)]
    public void The_window_is_centred_on_the_work_area_in_physical_pixels(
        int left, int top, int width, int height, double windowWidth, double windowHeight, double scale,
        int expectedLeft, int expectedTop, int expectedWidth, int expectedHeight)
    {
        var placed = SettingsPlacement.Centre(new PixelRect(left, top, width, height), windowWidth, windowHeight, scale);

        Assert.Equal(new PixelRect(expectedLeft, expectedTop, expectedWidth, expectedHeight), placed);
    }

    [Fact]
    public void A_window_taller_than_the_work_area_starts_at_its_top_and_fits_its_height()
    {
        var placed = SettingsPlacement.Centre(new PixelRect(0, 40, 1280, 600), 640, 720, 1.0);

        Assert.Equal(new PixelRect(320, 40, 640, 600), placed);
    }

    [Fact]
    public void A_window_wider_than_the_work_area_starts_at_its_left_edge()
    {
        var placed = SettingsPlacement.Centre(new PixelRect(-1280, 0, 500, 1000), 640, 720, 1.0);

        Assert.Equal(new PixelRect(-1280, 140, 640, 720), placed);
    }
}
