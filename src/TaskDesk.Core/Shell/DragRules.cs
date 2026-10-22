namespace TaskDesk.Core.Shell;

/// <summary>What releasing a drag does.</summary>
public enum DragRelease
{
    /// <summary>The release lands in the gap under the pointer.</summary>
    Drop,

    /// <summary>The release moves nothing.</summary>
    Cancel,
}

/// <summary>
/// The rules of the app's own reordering drag (ADR 0007), in device-independent pixels: when a press becomes a
/// drag, whether a release drops or cancels, and how much of a dragged block its picture shows.
/// </summary>
public static class DragRules
{
    /// <summary>The most rows a drag picture shows; a taller block shows its first rows, fading out.</summary>
    public const int PictureRowCap = 5;

    // The share of a faded picture's height, at its bottom, over which it fades out.
    private const double FadeShare = 0.4;

    /// <summary>
    /// True when the pointer has moved (<paramref name="dx"/>, <paramref name="dy"/>) from the press point past the
    /// drag threshold, which Windows gives as a distance on either side of the press point on each axis.
    /// </summary>
    public static bool HasPassedThreshold(double dx, double dy, double thresholdX, double thresholdY) =>
        Math.Abs(dx) > thresholdX || Math.Abs(dy) > thresholdY;

    /// <summary>
    /// A release at (<paramref name="x"/>, <paramref name="y"/>), relative to a list of the given size, drops over the
    /// list and cancels outside it; the right and bottom edges count as outside.
    /// </summary>
    public static DragRelease ReleaseAt(double x, double y, double width, double height) =>
        ReorderGeometry.HasLeft(x, y, width, height) ? DragRelease.Cancel : DragRelease.Drop;

    /// <summary>How far in from a list's top or bottom edge a drag scrolls it.</summary>
    public const double EdgeScrollBand = 40;

    /// <summary>How fast a drag scrolls a list with the pointer on its edge, in DIPs a second.</summary>
    public const double EdgeScrollFullSpeed = 800;

    /// <summary>
    /// How fast a drag with the pointer at (<paramref name="x"/>, <paramref name="y"/>) scrolls a list of the given
    /// size, in DIPs a second: negative (up) within <see cref="EdgeScrollBand"/> of its top edge, positive (down)
    /// within it of its bottom edge, at <see cref="EdgeScrollFullSpeed"/> on the edge and falling evenly to nothing
    /// at the band's inner side. Nothing scrolls elsewhere, outside the list included.
    /// </summary>
    public static double EdgeScrollSpeed(double x, double y, double width, double height)
    {
        if (ReorderGeometry.HasLeft(x, y, width, height))
        {
            return 0;
        }

        var fromTop = y;
        var fromBottom = height - y;
        return fromTop <= fromBottom
            ? -Share(fromTop) * EdgeScrollFullSpeed
            : Share(fromBottom) * EdgeScrollFullSpeed;

        static double Share(double fromEdge) => Math.Max(0, 1 - (fromEdge / EdgeScrollBand));
    }

    /// <summary>How many of a dragged block's <paramref name="rows"/> its picture shows.</summary>
    public static int PictureRows(int rows) => Math.Min(rows, PictureRowCap);

    /// <summary>True when a block of <paramref name="rows"/> is taller than its picture, which then fades out.</summary>
    public static bool PictureFades(int rows) => rows > PictureRowCap;

    /// <summary>
    /// The opacity at <paramref name="y"/> of a fading picture <paramref name="height"/> tall: whole above its bottom
    /// two fifths, then falling evenly to nothing at its bottom edge.
    /// </summary>
    public static double FadeAlpha(double y, double height)
    {
        var fadeTop = height * (1 - FadeShare);
        return y <= fadeTop ? 1 : Math.Clamp((height - y) / (height - fadeTop), 0, 1);
    }
}
