using Microsoft.UI.Xaml;

namespace TaskDesk.Views.Tests;

/// <summary>Shows an element alone in a live window, so its templates apply; dispose closes the window.</summary>
public sealed class Hosted : IDisposable
{
    private readonly Window _window;

    private Hosted(Window window) => _window = window;

    public static async Task<Hosted> ShowAsync(FrameworkElement element, CancellationToken cancellationToken)
    {
        var window = new Window { Content = element };
        var hosted = new Hosted(window);
        try
        {
            window.Activate();
            await UiWait.UntilAsync(() => element.IsLoaded, () => "The hosted element never loaded.", cancellationToken);
            element.UpdateLayout();
            return hosted;
        }
        catch
        {
            hosted.Dispose();
            throw;
        }
    }

    public void Dispose() => _window.Close();
}
