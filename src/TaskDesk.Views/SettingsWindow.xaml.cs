using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TaskDesk.Core.Shell;
using TaskDesk.Presentation;
using Windows.Graphics;
using WinRT.Interop;

namespace TaskDesk.Views;

/// <summary>
/// "TaskDesk Settings": the setting cards in a window of their own, 640 by 720 DIPs, centred on the monitor it
/// opens on. The shell keeps one open and registers it with the theme service.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    public const double DefaultWidth = 640;
    public const double DefaultHeight = 720;
    public const double MinimumWidth = 480;
    public const double MinimumHeight = 400;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        View = new SettingsView(viewModel);
        Content = View;

        var scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) is var dpi and > 0 ? dpi / 96.0 : 1.0;
        var presenter = OverlappedPresenter.Create();
        presenter.PreferredMinimumWidth = WindowLayoutCalculator.ToPhysical(MinimumWidth, scale);
        presenter.PreferredMinimumHeight = WindowLayoutCalculator.ToPhysical(MinimumHeight, scale);
        AppWindow.SetPresenter(presenter);

        var width = WindowLayoutCalculator.ToPhysical(DefaultWidth, scale);
        var height = WindowLayoutCalculator.ToPhysical(DefaultHeight, scale);
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.MoveAndResize(new RectInt32(
            work.X + ((work.Width - width) / 2),
            work.Y + Math.Max(0, (work.Height - height) / 2),
            width,
            Math.Min(height, work.Height)));
    }

    public SettingsView View { get; }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);
}
