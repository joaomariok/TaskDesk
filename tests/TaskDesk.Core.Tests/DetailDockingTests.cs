using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

/// <summary>
/// The detail pane docks beside the list in a window wider than 1100 px and overlays the list at 1100 px and
/// below; overlaid it is at least 320 px wide; dragging its splitter resizes it within its limits.
/// </summary>
public sealed class DetailDockingTests
{
    [Theory]
    [InlineData(600, false)]
    [InlineData(1099.5, false)]
    [InlineData(1100, false)]
    [InlineData(1100.5, true)]
    [InlineData(1400, true)]
    public void The_pane_docks_only_above_1100_px(double width, bool docked)
    {
        Assert.Equal(docked, PaneLayout.IsDetailDocked(width));
    }

    [Fact]
    public void A_window_not_measured_yet_docks_the_pane()
    {
        Assert.True(PaneLayout.IsDetailDocked(0));
        Assert.True(PaneLayout.IsDetailDocked(double.NaN));
    }

    [Theory]
    [InlineData(260, 320)]
    [InlineData(320, 320)]
    [InlineData(450, 450)]
    [InlineData(900, 600)]
    public void An_overlaid_pane_is_at_least_320_px_wide_and_within_its_limits(double width, double overlay)
    {
        Assert.Equal(overlay, PaneLayout.DetailOverlayWidth(width));
    }
}
