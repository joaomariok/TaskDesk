using Microsoft.UI;
using Microsoft.UI.Windowing;
using TaskDesk.Core.Shell;
using TaskDesk.Views;
using Windows.Graphics;

namespace TaskDesk.App.Shell;

/// <summary>
/// Restores and saves the main window's size, position and state through the settings store. The stored
/// layout is in device-independent pixels, as the WPF app writes it; <see cref="AppWindow"/> works in
/// physical pixels, so each monitor's rectangle converts at that monitor's own scale
/// (<see cref="WindowLayoutCalculator.ToLayout"/>, <see cref="WindowLayoutCalculator.Place"/>).
/// The sidebar's and the detail pane's widths are restored and saved with it (<see cref="PaneLayout"/>).
/// </summary>
internal sealed class WindowLayoutService(IShellSettingsStore store)
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
            var bounds = WindowLayoutCalculator.Place(saved, Monitors());
            appWindow.MoveAndResize(new RectInt32(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
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
            Monitors());
        store.Save(settings with
        {
            Layout = layout with { PaneWidths = PaneLayout.Update(settings.Layout?.PaneWidths, window.MeasurePaneWidths()) },
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
