namespace TaskDesk.Core.Shell;

/// <summary>A rectangle in physical (device) pixels.</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height);

/// <summary>
/// Pure window-geometry calculations. The stored <see cref="WindowLayout"/> is in device-independent
/// pixels (DIPs); a shell that places windows in physical pixels converts at the monitor's scale
/// (1.0 at 100%, 1.25 at 125%).
/// </summary>
public static class WindowLayoutCalculator
{
    /// <summary>Converts DIPs to whole physical pixels, rounding halves away from zero.</summary>
    public static int ToPhysical(double dips, double scale)
    {
        RequireScale(scale);
        return (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero);
    }

    /// <summary>Converts physical pixels to DIPs.</summary>
    public static double ToDips(int pixels, double scale)
    {
        RequireScale(scale);
        return pixels / scale;
    }

    /// <summary>Converts a layout's rectangle to physical pixels at the given scale.</summary>
    public static PixelRect ToPhysical(WindowLayout layout, double scale) => new(
        ToPhysical(layout.Left, scale),
        ToPhysical(layout.Top, scale),
        ToPhysical(layout.Width, scale),
        ToPhysical(layout.Height, scale));

    /// <summary>
    /// Returns a layout that respects the minimum size and lies fully inside the given screen area,
    /// so a window saved on a monitor that is gone is still reachable. All values are in DIPs.
    /// </summary>
    public static WindowLayout Fit(
        WindowLayout layout, double screenLeft, double screenTop, double screenWidth, double screenHeight)
    {
        var width = Math.Max(WindowLayout.MinWidth, Math.Min(layout.Width, screenWidth));
        var height = Math.Max(WindowLayout.MinHeight, Math.Min(layout.Height, screenHeight));
        var left = Math.Clamp(layout.Left, screenLeft, Math.Max(screenLeft, screenLeft + screenWidth - width));
        var top = Math.Clamp(layout.Top, screenTop, Math.Max(screenTop, screenTop + screenHeight - height));
        return layout with { Left = left, Top = top, Width = width, Height = height };
    }

    private static void RequireScale(double scale)
    {
        if (!(scale > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The scale must be positive.");
        }
    }
}
