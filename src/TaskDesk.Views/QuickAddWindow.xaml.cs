using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TaskDesk.Core.Shell;
using TaskDesk.Presentation;
using Windows.Foundation;
using Windows.Graphics;

namespace TaskDesk.Views;

/// <summary>
/// The quick-add card the global hotkey and the tray open, centred on the monitor that holds the mouse cursor.
/// It closes after a save, on Escape and when it loses activation.
/// </summary>
public sealed partial class QuickAddWindow : Window
{
    /// <summary>The card's width in DIPs; its height follows the content.</summary>
    public const double CardWidth = 420;

    // Until the content is measured: about the card's height at the default text size.
    private const double EstimatedHeight = 230;

    private bool _closing;
    private bool _closed;

    public QuickAddWindow(QuickAddViewModel viewModel)
    {
        InitializeComponent();
        View = new QuickAddView(viewModel);
        Content = View;

        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        PlaceOnCursorMonitor(EstimatedHeight);

        viewModel.CloseRequested += CloseOnce;
        View.Loaded += (_, _) =>
        {
            View.Measure(new Size(CardWidth, double.PositiveInfinity));
            PlaceOnCursorMonitor(View.DesiredSize.Height);
            View.FocusTitle();
        };
        // Clicking anywhere else dismisses the card, once it has had the foreground.
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                HasBeenActivated = true;
            }
            else if (HasBeenActivated)
            {
                viewModel.CancelCommand.Execute(null);
            }
        };
        Closed += (_, _) =>
        {
            _closing = true;
            _closed = true;
            viewModel.CloseRequested -= CloseOnce;
        };
    }

    public QuickAddView View { get; }

    /// <summary>True once the card has been activated (had the foreground).</summary>
    public bool HasBeenActivated { get; private set; }

    private void CloseOnce()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        // Not from inside the key or activation event that asked for it: close once that event has returned.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed)
            {
                Close();
            }
        });
    }

    private void PlaceOnCursorMonitor(double heightDips)
    {
        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        var scale = GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
        var work = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest).WorkArea;
        var placed = QuickAddPlacement.Centre(
            new PixelRect(work.X, work.Y, work.Width, work.Height), CardWidth, heightDips, scale);
        AppWindow.MoveAndResize(new RectInt32(placed.Left, placed.Top, placed.Width, placed.Height));
    }

    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(NativePoint point, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
}
