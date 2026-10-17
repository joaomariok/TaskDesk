using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

/// <summary>Below 900 px the sidebar is the icon rail; at 900 px and wider it is the full sidebar.</summary>
public sealed class SidebarRailTests
{
    [Theory]
    [InlineData(600, true)]
    [InlineData(899.5, true)]
    [InlineData(900, false)]
    [InlineData(1400, false)]
    public void The_sidebar_is_a_rail_only_below_900_px(double width, bool rail)
    {
        Assert.Equal(rail, PaneLayout.IsRail(width));
    }

    [Fact]
    public void A_window_not_measured_yet_shows_the_full_sidebar()
    {
        Assert.False(PaneLayout.IsRail(0));
        Assert.False(PaneLayout.IsRail(double.NaN));
    }
}
