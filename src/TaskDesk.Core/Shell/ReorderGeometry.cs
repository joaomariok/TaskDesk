namespace TaskDesk.Core.Shell;

/// <summary>The top and height of one realised row, in device-independent pixels, relative to its list.</summary>
public readonly record struct RowSpan(double Top, double Height)
{
    public double Bottom => Top + Height;
}

/// <summary>
/// The arithmetic of dragging inside a list, in device-independent pixels: which gap a drop lands in, where the
/// insertion line sits, whether the pointer has left the list, and which row is under it. Rows are given by index;
/// a row with no container (not realised) is null and is skipped.
/// </summary>
public static class ReorderGeometry
{
    /// <summary>
    /// The gap a drop at <paramref name="y"/> lands in (0 is before the first row, the row count after the last):
    /// over a row's top half the gap before it, over its bottom half the gap after it, past the rows the last gap.
    /// </summary>
    public static int GapAt(IReadOnlyList<RowSpan?> rows, double y)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i] is { } row && y < row.Top + row.Height / 2)
            {
                return i;
            }
        }

        return rows.Count;
    }

    /// <summary>
    /// The offset of the insertion line for <paramref name="gap"/>: the top of the row after the gap, or the bottom
    /// of the last row for the last gap. Null when that row has no container.
    /// </summary>
    public static double? LineOffset(IReadOnlyList<RowSpan?> rows, int gap)
    {
        if (gap < rows.Count)
        {
            return rows[gap]?.Top;
        }

        return rows.Count > 0 ? rows[^1]?.Bottom : null;
    }

    /// <summary>
    /// True when the point (<paramref name="x"/>, <paramref name="y"/>) is outside a list of the given size; the
    /// right and bottom edges count as outside.
    /// </summary>
    public static bool HasLeft(double x, double y, double width, double height) =>
        x < 0 || y < 0 || x >= width || y >= height;

    /// <summary>The index of the row whose span holds <paramref name="y"/>, or null over no row.</summary>
    public static int? RowAt(IReadOnlyList<RowSpan?> rows, double y)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i] is { } row && y >= row.Top && y < row.Bottom)
            {
                return i;
            }
        }

        return null;
    }
}
