using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using TaskDesk.Core.Shell;
using TaskDesk.Views;
using Windows.Graphics;

namespace TaskDesk.App.Shell;

/// <summary>
/// Restores and saves the main window's size, position and state through the settings store. The stored
/// layout is in device-independent pixels exactly as the (system-DPI-aware) WPF app writes it; <see cref="AppWindow"/>
/// works in physical pixels, so it converts with each monitor's scale and the system scale
/// (<see cref="WindowLayoutCalculator.ToLayout"/>, <see cref="WindowLayoutCalculator.Place"/>).
/// The sidebar's and the detail pane's widths are restored and saved with it (<see cref="PaneLayout"/>).
/// </summary>
internal sealed class WindowLayoutService(IShellSettingsStore store, ILogger<WindowLayoutService> logger)
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
            var monitors = Monitors();
            var bounds = WindowLayoutCalculator.Place(saved, monitors, WindowScale.OfSystem());
            if (!MoveAndResize(appWindow, bounds, monitors))
            {
                logger.LogDebug("The window is hidden at another scale than its monitor's; its size is set again once shown");
                FinishWhenShown(window, bounds);
            }

            if (saved.Maximized && appWindow.Presenter is OverlappedPresenter presenter)
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

        var settings = store.Load();
        var layout = WindowLayoutCalculator.ToLayout(
            new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized },
            Monitors(),
            WindowScale.OfSystem());
        store.Save(settings with
        {
            Layout = layout with { PaneWidths = PaneLayout.Update(settings.Layout?.PaneWidths, window.MeasurePaneWidths()) },
        });
    }

    // Moving the window onto a monitor at another scale changes its DPI, and Windows rescales it as it does
    // (WindowLayoutCalculator.RestoreMoves): it first moves onto that monitor, then to the bounds. Returns whether
    // the window then has the monitor's DPI (GetDpiForWindow); a hidden window may get it only once shown, and
    // Windows then rescales it away from the bounds.
    private static bool MoveAndResize(AppWindow appWindow, PixelRect bounds, IReadOnlyList<MonitorArea> monitors)
    {
        var hwnd = Win32Interop.GetWindowFromWindowId(appWindow.Id);
        foreach (var move in WindowLayoutCalculator.RestoreMoves(bounds, monitors, WindowScale.OfWindowHandle(hwnd)))
        {
            appWindow.MoveAndResize(ToRect(move));
        }

        return WindowLayoutCalculator.HasMonitorScale(bounds, monitors, WindowScale.OfWindowHandle(hwnd));
    }

    // Once the window is first shown and its DPI change has run (queued after the show), puts it at the bounds again
    // at the DPI it has now. A maximized window is restored for the move, so its restored size is right.
    private void FinishWhenShown(MainWindow window, PixelRect bounds)
    {
        void OnChanged(AppWindow sender, AppWindowChangedEventArgs change)
        {
            if (!change.DidVisibilityChange || !sender.IsVisible)
            {
                return;
            }

            sender.Changed -= OnChanged;
            window.DispatcherQueue.TryEnqueue(() =>
            {
                var appWindow = window.AppWindow;
                var hwnd = Win32Interop.GetWindowFromWindowId(appWindow.Id);
                var scale = WindowScale.OfWindowHandle(hwnd);
                if (!WindowLayoutCalculator.HasMonitorScale(bounds, Monitors(), scale))
                {
                    logger.LogWarning(
                        "The window never took its monitor's scale (it is at {Scale}); its restored size may be off", scale);
                    return;
                }

                if (appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized } maximized)
                {
                    maximized.Restore();
                    appWindow.MoveAndResize(ToRect(bounds));
                    maximized.Maximize();
                }
                else
                {
                    appWindow.MoveAndResize(ToRect(bounds));
                }
            });
        }

        window.AppWindow.Changed += OnChanged;
    }

    private static RectInt32 ToRect(PixelRect rect) => new(rect.Left, rect.Top, rect.Width, rect.Height);

    private void RememberNormalBounds(AppWindow appWindow)
    {
        if (appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            _normalBounds = new RectInt32(
                appWindow.Position.X, appWindow.Position.Y, appWindow.Size.Width, appWindow.Size.Height);
        }
    }

    // Every monitor now attached, with its own scale.
    private static List<MonitorArea> Monitors()
    {
        var areas = DisplayArea.FindAll();
        var monitors = new List<MonitorArea>(areas.Count);
        // Indexed: enumerating the FindAll result with foreach is not reliable in the C# projection.
        for (var i = 0; i < areas.Count; i++)
        {
            monitors.Add(ToMonitor(areas[i]));
        }

        if (monitors.Count == 0)
        {
            monitors.Add(ToMonitor(DisplayArea.Primary));
        }

        return monitors;
    }

    private static MonitorArea ToMonitor(DisplayArea area)
    {
        var bounds = area.OuterBounds;
        var work = area.WorkArea;
        return new MonitorArea(
            new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            new PixelRect(work.X, work.Y, work.Width, work.Height),
            WindowScale.OfMonitor(Win32Interop.GetMonitorFromDisplayId(area.DisplayId)));
    }
}
