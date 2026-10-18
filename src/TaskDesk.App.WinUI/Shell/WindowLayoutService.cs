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
            var monitors = Monitors();
            MoveAndResize(appWindow, WindowLayoutCalculator.Place(saved, monitors, WindowScale.OfSystem()), monitors);
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
    // (WindowLayoutCalculator.RestoreMoves): it first moves onto that monitor, then, once GetDpiForWindow reports the
    // monitor's DPI, to the bounds. Should the DPI change not have arrived during the first move, the moves are worked
    // out again from the DPI the window has then; the last move always goes to the bounds.
    private static void MoveAndResize(AppWindow appWindow, PixelRect bounds, IReadOnlyList<MonitorArea> monitors)
    {
        var hwnd = Win32Interop.GetWindowFromWindowId(appWindow.Id);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var moves = WindowLayoutCalculator.RestoreMoves(bounds, monitors, WindowScale.OfWindowHandle(hwnd));
            appWindow.MoveAndResize(ToRect(moves[0]));
            if (moves.Count == 1)
            {
                return;
            }
        }

        appWindow.MoveAndResize(ToRect(bounds));
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
