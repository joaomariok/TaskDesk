using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TaskDesk.Core.Shell;
using TaskDesk.Presentation;
using Windows.Graphics;

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
        // The view model follows the pending update until the window goes.
        Closed += (_, _) => viewModel.Stop();

        var scale = WindowScale.Of(this);
        var presenter = OverlappedPresenter.Create();
        presenter.PreferredMinimumWidth = WindowLayoutCalculator.ToPhysical(MinimumWidth, scale);
        presenter.PreferredMinimumHeight = WindowLayoutCalculator.ToPhysical(MinimumHeight, scale);
        AppWindow.SetPresenter(presenter);

        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var placed = SettingsPlacement.Centre(
            new PixelRect(work.X, work.Y, work.Width, work.Height), DefaultWidth, DefaultHeight, scale);
        AppWindow.MoveAndResize(new RectInt32(placed.Left, placed.Top, placed.Width, placed.Height));
    }

    public SettingsView View { get; }
}
