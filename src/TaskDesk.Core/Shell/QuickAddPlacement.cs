namespace TaskDesk.Core.Shell;

/// <summary>Where the quick-add card opens: centred on the work area of the monitor that holds the mouse cursor.</summary>
public static class QuickAddPlacement
{
    /// <summary>
    /// Centres a card of the given size (DIPs) on <paramref name="workArea"/> (physical pixels) at the monitor's
    /// scale. A card taller or wider than the work area starts at its top or left edge, so its title box stays on screen.
    /// </summary>
    public static PixelRect Centre(PixelRect workArea, double widthDips, double heightDips, double scale)
    {
        var width = WindowLayoutCalculator.ToPhysical(widthDips, scale);
        var height = WindowLayoutCalculator.ToPhysical(heightDips, scale);
        var left = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
        var top = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);
        return new PixelRect(left, top, width, height);
    }
}
