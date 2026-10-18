using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TaskDesk.Views.Tests;

/// <summary>
/// Resolves theme resources as a live window draws them in Light or Dark, whatever theme the test machine uses,
/// and measures text contrast. The WinUI counterpart of the WPF tests' <c>FluentTheme</c> helper.
/// </summary>
public static class ThemeColours
{
    /// <summary>
    /// The colour of each key as an element under <paramref name="theme"/> draws it in a shown window: a brush key
    /// (such as "...Brush" or Fluent's "ToggleButtonForegroundChecked") through <c>{ThemeResource}</c>, any other
    /// key as a TextBlock style's foreground.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, Color>> ResolveAsync(
        ElementTheme theme, IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        var root = new StackPanel { RequestedTheme = theme };
        const string Ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        foreach (var key in keys)
        {
            root.Children.Add((FrameworkElement)XamlReader.Load(IsBrush(key)
                ? $$"""<Border xmlns="{{Ns}}" Tag="{{key}}" Width="4" Height="4" Background="{ThemeResource {{key}}}" />"""
                : $$"""<TextBlock xmlns="{{Ns}}" Tag="{{key}}" Text="x" Style="{StaticResource {{key}}}" />"""));
        }

        var window = new Window { Content = root };
        try
        {
            window.Activate();
            await UiWait.UntilAsync(() => root.IsLoaded, () => "The probe window never loaded.", cancellationToken);
            return root.Children.Cast<FrameworkElement>().ToDictionary(
                e => (string)e.Tag,
                e => ((SolidColorBrush)(e is Border border ? border.Background : ((TextBlock)e).Foreground)).Color);
        }
        finally
        {
            window.Close();
        }
    }

    private static bool IsBrush(string key) =>
        key.EndsWith("Brush", StringComparison.Ordinal)
        || Application.Current.Resources.TryGetValue(key, out var value) && value is Brush;

    /// <summary><paramref name="top"/> drawn over the opaque <paramref name="below"/>, as an opaque colour.</summary>
    public static Color Over(Color top, Color below)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a));
        return Color.FromArgb(255, Mix(top.R, below.R), Mix(top.G, below.G), Mix(top.B, below.B));
    }

    /// <summary>The WCAG contrast ratio of two opaque colours, from 1 to 21.</summary>
    public static double Contrast(Color x, Color y)
    {
        static double Luminance(Color c)
        {
            static double Channel(byte v) => v / 255.0 <= 0.04045 ? v / 255.0 / 12.92 : Math.Pow((v / 255.0 + 0.055) / 1.055, 2.4);
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        var (light, dark) = (Math.Max(Luminance(x), Luminance(y)), Math.Min(Luminance(x), Luminance(y)));
        return (light + 0.05) / (dark + 0.05);
    }

    public static Color Rgb(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
