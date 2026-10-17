using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public sealed class QuickAddPlacementTests
{
    [Theory]
    // Work area left, top, width, height; card size in DIPs; scale; expected physical rectangle.
    [InlineData(0, 0, 1920, 1040, 420, 200, 1.00, 750, 420, 420, 200)]
    [InlineData(0, 0, 2560, 1400, 420, 200, 1.25, 1017, 575, 525, 250)]
    // A monitor left of the primary one, with the taskbar on top (work area starts below it).
    [InlineData(-1920, 48, 1920, 1032, 420, 200, 1.00, -1170, 464, 420, 200)]
    // An odd remainder rounds the offset down, like the WPF window did.
    [InlineData(0, 0, 1001, 601, 420, 200, 1.00, 290, 200, 420, 200)]
    public void The_card_is_centred_on_the_work_area_in_physical_pixels(
        int left, int top, int width, int height, double cardWidth, double cardHeight, double scale,
        int expectedLeft, int expectedTop, int expectedWidth, int expectedHeight)
    {
        var placed = QuickAddPlacement.Centre(new PixelRect(left, top, width, height), cardWidth, cardHeight, scale);

        Assert.Equal(new PixelRect(expectedLeft, expectedTop, expectedWidth, expectedHeight), placed);
    }

    [Fact]
    public void A_card_taller_than_the_work_area_starts_at_its_top()
    {
        var placed = QuickAddPlacement.Centre(new PixelRect(0, 40, 800, 300), 420, 400, 1.0);

        Assert.Equal(new PixelRect(190, 40, 420, 400), placed);
    }
}
