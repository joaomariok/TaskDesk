using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public class ReorderGeometryTests
{
    // Three rows of height 40 at tops 0, 40 and 80.
    private static readonly RowSpan?[] Three =
    [
        new RowSpan(0, 40), new RowSpan(40, 40), new RowSpan(80, 40),
    ];

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(19.9, 0)]
    [InlineData(20, 1)]
    [InlineData(39.9, 1)]
    [InlineData(40, 1)]
    [InlineData(59.9, 1)]
    [InlineData(60, 2)]
    [InlineData(99.9, 2)]
    [InlineData(100, 3)]
    [InlineData(500, 3)]
    public void GapAt_picks_the_gap_before_a_top_half_and_after_a_bottom_half(double y, int expected) =>
        Assert.Equal(expected, ReorderGeometry.GapAt(Three, y));

    [Fact]
    public void GapAt_skips_a_row_with_no_container()
    {
        RowSpan?[] rows = [new RowSpan(0, 40), null, new RowSpan(80, 40)];

        Assert.Equal(2, ReorderGeometry.GapAt(rows, 60));
        Assert.Equal(3, ReorderGeometry.GapAt(rows, 110));
    }

    [Fact]
    public void GapAt_is_zero_for_an_empty_list() =>
        Assert.Equal(0, ReorderGeometry.GapAt([], 10));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 40)]
    [InlineData(2, 80)]
    [InlineData(3, 120)]
    public void LineOffset_is_the_top_of_the_row_after_the_gap_or_the_bottom_of_the_last(int gap, double expected) =>
        Assert.Equal(expected, ReorderGeometry.LineOffset(Three, gap));

    [Fact]
    public void LineOffset_is_none_when_the_row_after_the_gap_has_no_container()
    {
        RowSpan?[] rows = [new RowSpan(0, 40), null, new RowSpan(80, 40)];

        Assert.Null(ReorderGeometry.LineOffset(rows, 1));
    }

    [Fact]
    public void LineOffset_is_none_when_the_last_row_has_no_container()
    {
        RowSpan?[] rows = [new RowSpan(0, 40), null];

        Assert.Null(ReorderGeometry.LineOffset(rows, 2));
    }

    [Fact]
    public void LineOffset_is_none_for_an_empty_list() =>
        Assert.Null(ReorderGeometry.LineOffset([], 0));

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(199.9, 119.9, false)]
    [InlineData(-0.1, 10, true)]
    [InlineData(10, -0.1, true)]
    [InlineData(200, 10, true)]
    [InlineData(10, 120, true)]
    public void HasLeft_counts_the_far_edges_as_outside(double x, double y, bool expected) =>
        Assert.Equal(expected, ReorderGeometry.HasLeft(x, y, width: 200, height: 120));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(39.9, 0)]
    [InlineData(40, 1)]
    [InlineData(119.9, 2)]
    public void RowAt_is_the_row_whose_span_holds_the_point(double y, int expected) =>
        Assert.Equal(expected, ReorderGeometry.RowAt(Three, y));

    [Theory]
    [InlineData(-1)]
    [InlineData(120)]
    [InlineData(300)]
    public void RowAt_is_none_outside_every_row(double y) =>
        Assert.Null(ReorderGeometry.RowAt(Three, y));

    [Fact]
    public void RowAt_ignores_a_row_with_no_container()
    {
        RowSpan?[] rows = [new RowSpan(0, 40), null, new RowSpan(80, 40)];

        Assert.Null(ReorderGeometry.RowAt(rows, 50));
        Assert.Equal(2, ReorderGeometry.RowAt(rows, 90));
    }

    [Fact]
    public void RowAt_is_none_for_an_empty_list() =>
        Assert.Null(ReorderGeometry.RowAt([], 10));
}
