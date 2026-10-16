using System.Windows;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Shell;

/// <summary>Restores and saves the main window's size, position and state through the settings store.</summary>
internal sealed class WindowLayoutService(IShellSettingsStore store)
{
    public void Restore(Window window)
    {
        if (store.Load().Layout is not { } saved)
        {
            return;
        }

        var fitted = saved.FitTo(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = fitted.Left;
        window.Top = fitted.Top;
        window.Width = fitted.Width;
        window.Height = fitted.Height;
        if (fitted.Maximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    public void Save(Window window)
    {
        // RestoreBounds is the normal-state rectangle even when minimized or maximized.
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        if (bounds.IsEmpty || double.IsNaN(bounds.Left) || double.IsNaN(bounds.Width))
        {
            return;
        }

        var settings = store.Load();
        store.Save(settings with
        {
            Layout = new WindowLayout(
                bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                window.WindowState == WindowState.Maximized,
                settings.Layout?.PaneWidths),
        });
    }
}
