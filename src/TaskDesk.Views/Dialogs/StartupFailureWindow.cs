using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TaskDesk.Core.Shell;
using Windows.Graphics;

namespace TaskDesk.Views.Dialogs;

/// <summary>
/// A small window of its own for the start-up failure dialog: a content dialog needs a shown window, and the
/// main window does not exist yet when the database cannot be opened.
/// </summary>
public sealed class StartupFailureWindow : Window
{
    private const int WidthDips = 560;
    private const int HeightDips = 400;

    private readonly Grid _root = new();

    public StartupFailureWindow()
    {
        Title = StartupFailureText.Title;
        if (Application.Current.Resources.TryGetValue("SolidBackgroundFillColorBaseBrush", out var brush))
        {
            _root.Background = (Brush)brush;
        }

        Content = _root;
    }

    /// <summary>Shows the window and the message on it, and closes the window when the owner presses OK.</summary>
    public async Task ShowAsync(string message)
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _root.Loaded += (_, _) => loaded.TrySetResult();
        Activate();
        await loaded.Task;
        FitAndCentre(_root.XamlRoot.RasterizationScale);

        await new DialogService(() => this).ShowMessageAsync(StartupFailureText.Title, message);
        Close();
    }

    private void FitAndCentre(double scale)
    {
        var width = (int)Math.Round(WidthDips * scale);
        var height = (int)Math.Round(HeightDips * scale);
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.MoveAndResize(new RectInt32(
            area.X + Math.Max(0, (area.Width - width) / 2),
            area.Y + Math.Max(0, (area.Height - height) / 2),
            Math.Min(width, area.Width),
            Math.Min(height, area.Height)));
    }
}
