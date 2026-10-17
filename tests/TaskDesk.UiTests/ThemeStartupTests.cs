using System.Drawing;

namespace TaskDesk.UiTests;

/// <summary>
/// Theme start-up smoke in a real process: with the theme setting at Light or Dark, the app starts, quick add
/// gets ready and the window draws in that theme whatever the Windows app mode is. Framework-neutral: they run
/// against the WPF build by default and the WinUI build with ui-tests.ps1 -WinUI.
/// </summary>
public sealed class ThemeStartupTests
{
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_app_starts_in_the_saved_theme(string theme)
    {
        // Start waits until the window shows and quick add is ready, with start-up focus in it.
        using var app = AppSession.Start(theme: theme);
        app.Window.SetForeground();

        var luminance = 0.0;
        AppSession.WaitUntil(
            () =>
            {
                luminance = BackgroundLuminance(app);
                return theme == "Light" ? luminance > 0.6 : luminance < 0.4;
            },
            () => $"{theme}: the window background has luminance {luminance:0.00} (0 is black, 1 is white).");
    }

    // The mean relative luminance of a strip near the bottom of the window, below the (empty) Task list: only
    // the window background (Mica or the theme's window colour) shows there.
    private static double BackgroundLuminance(AppSession app)
    {
        var bounds = app.Window.BoundingRectangle;
        var strip = new Rectangle(bounds.Left + 40, bounds.Bottom - 60, Math.Max(1, bounds.Width - 80), 30);
        using var image = FlaUI.Core.Capturing.Capture.Rectangle(strip);
        var bitmap = image.Bitmap;
        var total = 0.0;
        var count = 0;
        for (var x = 0; x < bitmap.Width; x += 8)
        {
            for (var y = 0; y < bitmap.Height; y += 4)
            {
                var c = bitmap.GetPixel(x, y);
                total += (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
                count++;
            }
        }

        return total / count;
    }
}
