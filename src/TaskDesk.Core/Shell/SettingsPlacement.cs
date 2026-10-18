namespace TaskDesk.Core.Shell;

/// <summary>Where the Settings window opens: centred on the work area of the monitor it opens on.</summary>
public static class SettingsPlacement
{
    /// <summary>
    /// Centres a window of the given size (DIPs) on <paramref name="workArea"/> (physical pixels) at the monitor's
    /// scale. A window wider than the work area starts at its left edge, and one taller than it starts at its top and
    /// is cut to its height, so the title bar stays on screen.
    /// </summary>
    public static PixelRect Centre(PixelRect workArea, double widthDips, double heightDips, double scale)
    {
        // The quick-add card's placement, which keeps the left and top edges on the work area.
        var placed = QuickAddPlacement.Centre(workArea, widthDips, heightDips, scale);
        return placed with { Height = Math.Min(placed.Height, workArea.Height) };
    }
}
