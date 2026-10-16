namespace TaskDesk.Core.Tasks;

/// <summary>Pure rules for the manual order (SortOrder) of Tasks within a List.</summary>
public static class ManualOrder
{
    /// <summary>When a neighbouring gap drops below this, the whole List is renormalised.</summary>
    public const double MinimumGap = 1e-6;

    /// <summary>The position between two neighbours: their average, or one step beyond an end.</summary>
    public static double Between(double? previous, double? next) => (previous, next) switch
    {
        (null, null) => 1,
        (null, { } n) => n - 1,
        ({ } p, null) => p + 1,
        ({ } p, { } n) => (p + n) / 2,
    };

    /// <summary>
    /// Moves the item at <paramref name="from"/> so that it ends at index <paramref name="to"/>.
    /// <paramref name="orders"/> are the current SortOrder values in ascending order. Returns the new
    /// values in the new sequence: only the moved item changes, unless a neighbouring gap would fall
    /// below <see cref="MinimumGap"/>, in which case every value is renormalised to 1, 2, 3...
    /// </summary>
    public static double[] Move(IReadOnlyList<double> orders, int from, int to)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, orders.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(to);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(to, orders.Count);

        var result = orders.ToList();
        result.RemoveAt(from);

        double? previous = to > 0 ? result[to - 1] : null;
        double? next = to < result.Count ? result[to] : null;
        var position = from == to ? orders[from] : Between(previous, next);
        result.Insert(to, position);

        var tooClose = (previous is { } p && position - p < MinimumGap)
            || (next is { } n && n - position < MinimumGap);
        return tooClose && from != to ? Renormalise(result.Count) : [.. result];
    }

    private static double[] Renormalise(int count) => [.. Enumerable.Range(1, count).Select(i => (double)i)];
}
