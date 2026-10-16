using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public class ManualOrderTests
{
    [Theory]
    [InlineData(null, null, 1.0)]
    [InlineData(null, 5.0, 4.0)]
    [InlineData(5.0, null, 6.0)]
    [InlineData(1.0, 2.0, 1.5)]
    [InlineData(1.0, 4.0, 2.5)]
    public void Between_places_a_position_relative_to_its_neighbours(double? previous, double? next, double expected)
        => Assert.Equal(expected, ManualOrder.Between(previous, next));

    [Theory]
    // orders, from, to, expected orders in the new sequence
    [InlineData(new[] { 1.0, 2.0, 3.0 }, 2, 0, new[] { 0.0, 1.0, 2.0 })]
    [InlineData(new[] { 1.0, 2.0, 3.0 }, 0, 2, new[] { 2.0, 3.0, 4.0 })]
    [InlineData(new[] { 1.0, 2.0, 3.0 }, 0, 1, new[] { 2.0, 2.5, 3.0 })]
    [InlineData(new[] { 1.0, 2.0, 3.0, 4.0 }, 3, 1, new[] { 1.0, 1.5, 2.0, 3.0 })]
    [InlineData(new[] { 1.0, 2.0, 3.0 }, 1, 1, new[] { 1.0, 2.0, 3.0 })]
    public void Move_gives_the_moved_item_the_average_of_its_new_neighbours(
        double[] orders, int from, int to, double[] expected)
        => Assert.Equal(expected, ManualOrder.Move(orders, from, to));

    [Fact]
    public void Move_leaves_the_other_orders_untouched_when_the_gap_is_large_enough()
    {
        var result = ManualOrder.Move([10.0, 20.0, 30.0, 40.0], 3, 1);

        Assert.Equal([10.0, 15.0, 20.0, 30.0], result);
    }

    [Fact]
    public void Move_renormalises_to_whole_numbers_when_the_gap_drops_below_the_threshold()
    {
        // Moving the last item between two neighbours that are already 1.5e-6 apart
        // would leave a gap of 0.75e-6, below the 1e-6 threshold.
        double[] orders = [1.0, 1.0 + 1.5e-6, 5.0];

        var result = ManualOrder.Move(orders, 2, 1);

        Assert.Equal([1.0, 2.0, 3.0], result);
    }

    [Fact]
    public void Move_does_not_renormalise_when_the_gap_stays_at_the_threshold_or_above()
    {
        double[] orders = [1.0, 1.0 + 4e-6, 5.0];

        var result = ManualOrder.Move(orders, 2, 1);

        Assert.Equal(1.0 + 2e-6, result[1], 12);
        Assert.Equal(1.0, result[0]);
        Assert.Equal(1.0 + 4e-6, result[2]);
    }

    [Fact]
    public void Repeated_moves_into_the_same_gap_eventually_renormalise_and_keep_the_order_strictly_increasing()
    {
        double[] orders = [1.0, 2.0, 3.0];

        // Drag the last item between the first two, over and over.
        for (var i = 0; i < 60; i++)
        {
            orders = ManualOrder.Move(orders, 2, 1);
            Assert.True(orders[0] < orders[1] && orders[1] < orders[2]);
            Assert.True(orders[1] - orders[0] >= ManualOrder.MinimumGap);
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(3, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 3)]
    public void Move_rejects_indexes_outside_the_list(int from, int to)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ManualOrder.Move([1.0, 2.0, 3.0], from, to));
}
