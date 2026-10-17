namespace TaskDesk.Core.Shell;

/// <summary>The default and allowed widths of a resizable pane of the main window.</summary>
public sealed record PaneLimits(double Default, double Min, double Max)
{
    public double Clamp(double width) => Math.Clamp(width, Min, Max);
}

/// <summary>
/// The resizable panes of the main window (the sidebar and the detail pane) and the rules for the widths
/// remembered in <see cref="WindowLayout.PaneWidths"/>.
/// </summary>
public static class PaneLayout
{
    /// <summary>The sidebar with the Smart Views, Lists and Tags.</summary>
    public const string Navigation = "nav";

    /// <summary>The detail pane of the selected Task.</summary>
    public const string Detail = "detail";

    public static readonly PaneLimits NavigationLimits = new(Default: 180, Min: 140, Max: 400);
    public static readonly PaneLimits DetailLimits = new(Default: 260, Min: 220, Max: 600);

    /// <summary>The window width below which the sidebar shows as the icon rail.</summary>
    public const double RailBelowWidth = 900;

    /// <summary>
    /// True when a window <paramref name="width"/> device-independent pixels wide shows the sidebar as the
    /// icon rail. A window not measured yet (zero or not a number) shows the full sidebar.
    /// </summary>
    public static bool IsRail(double width) => width > 0 && width < RailBelowWidth;

    public static PaneLimits LimitsOf(string pane) => pane switch
    {
        Navigation => NavigationLimits,
        Detail => DetailLimits,
        _ => throw new ArgumentException($"Unknown pane '{pane}'.", nameof(pane)),
    };

    /// <summary>The width to show <paramref name="pane"/> at: the saved one within its limits, else the default.</summary>
    public static double WidthOf(WindowLayout? layout, string pane)
    {
        var limits = LimitsOf(pane);
        return layout?.PaneWidths?.GetValueOrDefault(pane) is { } saved && double.IsFinite(saved) && saved > 0
            ? limits.Clamp(saved)
            : limits.Default;
    }

    /// <summary>
    /// The pane widths to save: <paramref name="saved"/> with each measured known pane replaced by its width
    /// within its limits. A pane measured at zero or less is hidden (the detail pane without a selection),
    /// so it keeps its saved width.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Update(
        IReadOnlyDictionary<string, double>? saved, IReadOnlyDictionary<string, double> measured)
    {
        var result = new Dictionary<string, double>(saved ?? new Dictionary<string, double>());
        foreach (var (pane, width) in measured)
        {
            if (double.IsFinite(width) && width > 0)
            {
                result[pane] = LimitsOf(pane).Clamp(width);
            }
        }

        return result;
    }
}
