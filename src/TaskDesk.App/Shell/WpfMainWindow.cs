using System.Windows;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Shell;

/// <summary>Adapts the WPF main window to <see cref="IMainWindow"/>, marshalling to the UI thread.</summary>
internal sealed class WpfMainWindow(MainWindow window) : IMainWindow, IAppShutdown
{
    public bool IsVisible => window.Dispatcher.Invoke(() => window.IsVisible);

    public void ShowAndFocus() => window.Dispatcher.Invoke(() =>
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Focus();
    });

    public void Hide() => window.Dispatcher.Invoke(window.Hide);

    public void Shutdown() => Application.Current.Dispatcher.Invoke(Application.Current.Shutdown);
}
