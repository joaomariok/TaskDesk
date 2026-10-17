using System.Windows;
using System.Windows.Media;
using TaskDesk.App.Styles;

namespace TaskDesk.App.Tests;

/// <summary>
/// Reads the colours of Fluent's Light or Dark theme, whatever theme the test machine uses, and measures text
/// contrast, for the App tests.
/// </summary>
internal static class FluentTheme
{
    /// <summary>Fluent's colour and brush dictionary of <paramref name="theme"/>: "Light", "Dark" or "HC".</summary>
    public static ResourceDictionary Colours(string theme) => new()
    {
        Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Resources/Theme/{theme}.xaml"),
    };

    /// <summary>
    /// The colour of a Fluent brush key ("...Brush"), a Fluent colour key, or one of the app's meaning-named
    /// brushes (<see cref="FoundationResources"/>), in <paramref name="colours"/>.
    /// </summary>
    public static Color ColourOf(ResourceDictionary colours, string key) =>
        colours[key = FoundationResources.FluentKeyOf(key)] switch
        {
            SolidColorBrush brush => brush.Color,
            Color colour => colour,
            _ => (Color)colours[key.EndsWith("Brush", StringComparison.Ordinal) ? key[..^5] : key],
        };

    /// <summary>The resource key of a <c>{DynamicResource}</c> setter of <paramref name="style"/>, followed through BasedOn.</summary>
    public static string KeyOf(Style style, DependencyProperty property) =>
        style.Setters.OfType<Setter>().Where(s => s.Property == property)
            .Select(s => (string)((DynamicResourceExtension)s.Value).ResourceKey).FirstOrDefault()
        ?? KeyOf(style.BasedOn!, property);

    /// <summary><paramref name="top"/> drawn over the opaque <paramref name="below"/>, as an opaque colour.</summary>
    public static Color Over(Color top, Color below)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a));
        return Color.FromRgb(Mix(top.R, below.R), Mix(top.G, below.G), Mix(top.B, below.B));
    }

    /// <summary>The WCAG contrast ratio of two opaque colours, from 1 to 21.</summary>
    public static double Contrast(Color x, Color y)
    {
        double Luminance(Color c)
        {
            double Channel(byte v) => v / 255.0 <= 0.04045 ? v / 255.0 / 12.92 : Math.Pow((v / 255.0 + 0.055) / 1.055, 2.4);
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        var (light, dark) = (Math.Max(Luminance(x), Luminance(y)), Math.Min(Luminance(x), Luminance(y)));
        return (light + 0.05) / (dark + 0.05);
    }
}
