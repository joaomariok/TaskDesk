using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using TaskDesk.Core.Shell;
using TaskDesk.Views;
using Windows.Graphics;

namespace TaskDesk.App.Shell;

/// <summary>
/// Restores and saves the main window's size, position and state through the settings store. The stored
/// layout is in device-independent pixels, as the WPF app writes it; <see cref="AppWindow"/> works in
/// physical pixels, so values are converted at the window's DPI (<see cref="WindowLayoutCalculator"/>).
/// The sidebar's and the detail pane's widths are restored and saved with it (<see cref="PaneLayout"/>).
/// </summary>
internal sealed partial class WindowLayoutService(IShellSettingsStore store)
{
    // The last size and position the window had while neither minimized nor maximized.
    private RectInt32? _normalBounds;

    public void Restore(MainWindow window)
    {
        var appWindow = window.AppWindow;
        appWindow.Changed += (sender, _) => RememberNormalBounds(sender);
        var saved = store.Load().Layout;
        window.SetPaneWidths(saved);
        if (saved is not null)
        {
            var scale = ScaleOf(window);
            var fitted = WindowLayoutCalculator.Fit(
                saved,
                WindowLayoutCalculator.ToDips(GetSystemMetrics(SmXVirtualScreen), scale),
                WindowLayoutCalculator.ToDips(GetSystemMetrics(SmYVirtualScreen), scale),
                WindowLayoutCalculator.ToDips(GetSystemMetrics(SmCxVirtualScreen), scale),
                WindowLayoutCalculator.ToDips(GetSystemMetrics(SmCyVirtualScreen), scale));
            var bounds = WindowLayoutCalculator.ToPhysical(fitted, scale);
            appWindow.MoveAndResize(new RectInt32(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
            if (fitted.Maximized && appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }

        RememberNormalBounds(appWindow);
    }

    public void Save(MainWindow window)
    {
        var appWindow = window.AppWindow;
        RememberNormalBounds(appWindow);
        if (_normalBounds is not { } bounds || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var scale = ScaleOf(window);
        var settings = store.Load();
        store.Save(settings with
        {
            Layout = new WindowLayout(
                WindowLayoutCalculator.ToDips(bounds.X, scale),
                WindowLayoutCalculator.ToDips(bounds.Y, scale),
                WindowLayoutCalculator.ToDips(bounds.Width, scale),
                WindowLayoutCalculator.ToDips(bounds.Height, scale),
                appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized },
                PaneLayout.Update(settings.Layout?.PaneWidths, window.MeasurePaneWidths())),
        });
    }

    private void RememberNormalBounds(AppWindow appWindow)
    {
        if (appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            _normalBounds = new RectInt32(
                appWindow.Position.X, appWindow.Position.Y, appWindow.Size.Width, appWindow.Size.Height);
        }
    }

    // 1.0 at 100 % display scale, 1.25 at 125 %.
    private static double ScaleOf(MainWindow window) =>
        GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window)) / 96.0;

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);
}
