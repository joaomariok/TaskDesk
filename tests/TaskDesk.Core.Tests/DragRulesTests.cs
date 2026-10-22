using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public class DragRulesTests
{
    // Windows' default drag threshold: 4 px on either side of the press point.
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(4, 0, false)]
    [InlineData(-4, 4, false)]
    [InlineData(4.1, 0, true)]
    [InlineData(0, -4.1, true)]
    [InlineData(-10, 2, true)]
    public void A_press_becomes_a_drag_only_past_the_threshold_on_either_side(double dx, double dy, bool expected) =>
        Assert.Equal(expected, DragRules.HasPassedThreshold(dx, dy, thresholdX: 4, thresholdY: 4));

    [Fact]
    public void The_threshold_is_per_axis()
    {
        Assert.False(DragRules.HasPassedThreshold(5, 0, thresholdX: 6, thresholdY: 2));
        Assert.True(DragRules.HasPassedThreshold(0, 3, thresholdX: 6, thresholdY: 2));
    }

    [Theory]
    [InlineData(10, 10, DragRelease.Drop)]
    [InlineData(0, 0, DragRelease.Drop)]
    [InlineData(199.9, 119.9, DragRelease.Drop)]
    [InlineData(-1, 10, DragRelease.Cancel)]
    [InlineData(10, -1, DragRelease.Cancel)]
    [InlineData(200, 10, DragRelease.Cancel)]
    [InlineData(10, 120, DragRelease.Cancel)]
    public void A_release_over_the_list_drops_and_outside_it_cancels(double x, double y, DragRelease expected) =>
        Assert.Equal(expected, DragRules.ReleaseAt(x, y, width: 200, height: 120));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(40, 5)]
    public void The_picture_shows_at_most_five_rows(int rows, int expected) =>
        Assert.Equal(expected, DragRules.PictureRows(rows));

    [Fact]
    public void A_picture_of_five_rows_or_fewer_does_not_fade()
    {
        Assert.False(DragRules.PictureFades(5));
        Assert.True(DragRules.PictureFades(6));
    }

    // A faded picture 100 px tall fades over its bottom 40 px: fully shown above, gone at the bottom edge.
    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(59, 1.0)]
    [InlineData(60, 1.0)]
    [InlineData(80, 0.5)]
    [InlineData(100, 0.0)]
    public void A_faded_picture_fades_out_over_its_bottom_two_fifths(double y, double expected) =>
        Assert.Equal(expected, DragRules.FadeAlpha(y, height: 100), precision: 6);
}
