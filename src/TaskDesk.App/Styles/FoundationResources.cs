using System.Collections.Specialized;
using System.Windows;

namespace TaskDesk.App.Styles;

/// <summary>
/// Defines the meaning-named brushes of the theme foundation (<c>Styles/Foundation.xaml</c>) and keeps
/// them in step with the Fluent theme.
/// </summary>
/// <remarks>
/// Each brush is the Fluent theme brush that carries its meaning. XAML cannot alias one resource key to
/// another so that the alias follows a theme change: WPF swaps its whole Fluent dictionary on a
/// <c>ThemeMode</c>, system theme or high-contrast change, refreshing only references to keys in that
/// dictionary (and a <c>StaticResource</c> alias in a merged dictionary is unreliable). So the brushes
/// are set here, in the foundation dictionary, and set again after every swap; setting a key makes
/// every <c>DynamicResource</c> reference to it pick up the new brush. Use them through
/// <c>DynamicResource</c>.
/// </remarks>
internal static class FoundationResources
{
    /// <summary>Overdue Due text, icons and badges: Fluent's critical colour.</summary>
    public const string OverdueBrush = nameof(OverdueBrush);

    /// <summary>Due-today text and icons: the system accent, in Fluent's readable accent-text shade.</summary>
    public const string TodayBrush = nameof(TodayBrush);

    /// <summary>
    /// A List's own colour (data, one of six presets) draws its stripe and dot. This brush is the
    /// stroke of a colourless List's hollow ring, and stands in for List colours in high contrast.
    /// </summary>
    public const string ListStripeBrush = nameof(ListStripeBrush);

    private const string FileName = "Foundation.xaml";

    // Meaning-named key -> the Fluent theme brush it stands for.
    private static readonly (string Key, string FluentKey)[] Brushes =
    [
        (OverdueBrush, "SystemFillColorCriticalBrush"),
        (TodayBrush, "AccentTextFillColorPrimaryBrush"),
        (ListStripeBrush, "ControlStrongStrokeColorDefaultBrush"),
    ];

    /// <summary>
    /// Sets the brushes from the current theme and sets them again whenever Fluent swaps its
    /// dictionary. Call once at start-up, after <c>App.xaml</c> has loaded its resources.
    /// </summary>
    public static void Install(Application app)
    {
        var merged = app.Resources.MergedDictionaries;
        var foundation = merged.Single(d =>
            d.Source?.OriginalString.EndsWith(FileName, StringComparison.OrdinalIgnoreCase) == true);

        Refresh(app, foundation);
        if (merged is INotifyCollectionChanged changes)
        {
            changes.CollectionChanged += (_, _) => Refresh(app, foundation);
        }
    }

    private static void Refresh(Application app, ResourceDictionary foundation)
    {
        foreach (var (key, fluentKey) in Brushes)
        {
            if (app.TryFindResource(fluentKey) is { } brush && !ReferenceEquals(foundation[key], brush))
            {
                foundation[key] = brush;
            }
        }
    }
}
