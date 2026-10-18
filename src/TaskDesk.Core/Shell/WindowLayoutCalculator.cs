namespace TaskDesk.Core.Shell;

/// <summary>A rectangle in physical (device) pixels.</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height);

/// <summary>
/// One monitor as the shell sees it: its bounds and work area in physical pixels, and its scale
/// (1.0 at 100 %, 1.5 at 150 %).
/// </summary>
public readonly record struct MonitorArea(PixelRect Bounds, PixelRect WorkArea, double Scale);

/// <summary>
/// Pure window-geometry calculations. The stored <see cref="WindowLayout"/> is in device-independent
/// pixels (DIPs) as the WPF app writes them; a shell that places windows in physical pixels converts with
/// <see cref="ToLayout"/> and <see cref="Place"/>.
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

    // The stored layout means what the WPF app writes, so settings.json reads the same in both builds. That app is
    // system-DPI aware (it has no manifest, and WPF then opts the process in): its DIPs are the coordinates Windows
    // virtualizes for it, divided by the system scale S (the primary monitor's at sign-in). Windows virtualizes a
    // monitor whose scale s differs from S about the monitor's own top-left corner O, so a physical point p on it is
    // stored as O / S + (p - O) / s. On a monitor at the system scale that is the plain p / S.

    /// <summary>
    /// Converts a window's normal bounds (physical pixels) to the layout the WPF app would write for it, on the
    /// monitor that holds most of the window. <see cref="WindowLayout.PaneWidths"/> is left unset.
    /// </summary>
    /// <param name="systemScale">The system scale (the system DPI / 96).</param>
    public static WindowLayout ToLayout(
        PixelRect bounds, bool maximized, IReadOnlyList<MonitorArea> monitors, double systemScale)
    {
        RequireMonitors(monitors);
        RequireScale(systemScale);
        var monitor = Best(bounds.Left, bounds.Top, bounds.Width, bounds.Height, monitors, m => AsDip(m.Bounds));
        RequireScale(monitor.Scale);
        var origin = monitor.Bounds;
        return new WindowLayout(
            (origin.Left / systemScale) + ((bounds.Left - origin.Left) / monitor.Scale),
            (origin.Top / systemScale) + ((bounds.Top - origin.Top) / monitor.Scale),
            bounds.Width / monitor.Scale,
            bounds.Height / monitor.Scale,
            maximized);
    }

    /// <summary>
    /// Returns the normal bounds (physical pixels) for a layout as the WPF app writes it: on the monitor whose area
    /// in those coordinates holds most of it (else the nearest one), fitted to that monitor's work area.
    /// </summary>
    /// <param name="systemScale">The system scale (the system DPI / 96).</param>
    public static PixelRect Place(WindowLayout saved, IReadOnlyList<MonitorArea> monitors, double systemScale)
    {
        RequireMonitors(monitors);
        RequireScale(systemScale);
        var monitor = Best(saved.Left, saved.Top, saved.Width, saved.Height, monitors, m => DipArea(m.Bounds, m, systemScale));
        RequireScale(monitor.Scale);
        var work = DipArea(monitor.WorkArea, monitor, systemScale);
        var fitted = Fit(saved, work.Left, work.Top, work.Width, work.Height);
        var origin = monitor.Bounds;
        return new PixelRect(
            origin.Left + ToPhysical(fitted.Left - (origin.Left / systemScale), monitor.Scale),
            origin.Top + ToPhysical(fitted.Top - (origin.Top / systemScale), monitor.Scale),
            ToPhysical(fitted.Width, monitor.Scale),
            ToPhysical(fitted.Height, monitor.Scale));
    }

    /// <summary>
    /// The moves (physical pixels) that put a window at its restored <paramref name="bounds"/>. When the window's
    /// scale differs from that of the monitor holding most of the bounds, moving there changes the window's DPI, and
    /// Windows rescales it by (monitor scale / window scale) as it does. So the window first moves onto the monitor
    /// at a size that stays inside the bounds through that rescale; once it has the monitor's DPI, the second move
    /// lands exactly. On a monitor at the window's own scale one move is enough.
    /// </summary>
    /// <param name="windowScale">The window's scale now (its DPI / 96).</param>
    public static IReadOnlyList<PixelRect> RestoreMoves(PixelRect bounds, IReadOnlyList<MonitorArea> monitors, double windowScale)
    {
        RequireMonitors(monitors);
        RequireScale(windowScale);
        var monitor = Best(bounds.Left, bounds.Top, bounds.Width, bounds.Height, monitors, m => AsDip(m.Bounds));
        RequireScale(monitor.Scale);
        if (monitor.Scale == windowScale)
        {
            return [bounds];
        }

        var shrink = Math.Min(1.0, windowScale / monitor.Scale);
        var staging = bounds with
        {
            Width = Math.Max(1, ToPhysical(bounds.Width, shrink)),
            Height = Math.Max(1, ToPhysical(bounds.Height, shrink)),
        };
        return [staging, bounds];
    }

    private readonly record struct DipRect(double Left, double Top, double Width, double Height);

    // A monitor's area (physical pixels) in the WPF app's coordinates.
    private static DipRect DipArea(PixelRect area, MonitorArea monitor, double systemScale) => new(
        (monitor.Bounds.Left / systemScale) + ((area.Left - monitor.Bounds.Left) / monitor.Scale),
        (monitor.Bounds.Top / systemScale) + ((area.Top - monitor.Bounds.Top) / monitor.Scale),
        area.Width / monitor.Scale,
        area.Height / monitor.Scale);

    private static DipRect AsDip(PixelRect rect) => new(rect.Left, rect.Top, rect.Width, rect.Height);

    // The monitor whose area overlaps the rectangle most; with no overlap, the one whose centre is nearest.
    private static MonitorArea Best(
        double left, double top, double width, double height,
        IReadOnlyList<MonitorArea> monitors, Func<MonitorArea, DipRect> area)
    {
        var best = monitors[0];
        var bestOverlap = -1.0;
        var bestDistance = double.MaxValue;
        foreach (var monitor in monitors)
        {
            var a = area(monitor);
            var overlapX = Math.Min(left + width, a.Left + a.Width) - Math.Max(left, a.Left);
            var overlapY = Math.Min(top + height, a.Top + a.Height) - Math.Max(top, a.Top);
            var overlap = overlapX > 0 && overlapY > 0 ? overlapX * overlapY : 0;
            var dx = (left + (width / 2)) - (a.Left + (a.Width / 2));
            var dy = (top + (height / 2)) - (a.Top + (a.Height / 2));
            var distance = (dx * dx) + (dy * dy);
            if (overlap > bestOverlap || (overlap == bestOverlap && distance < bestDistance))
            {
                best = monitor;
                bestOverlap = overlap;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static void RequireMonitors(IReadOnlyList<MonitorArea> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        }
    }

    private static void RequireScale(double scale)
    {
        if (!(scale > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The scale must be positive.");
        }
    }
}
