using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TaskDesk.Core.Shell;
using TaskDesk.Presentation;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

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
        DropSystemFrame(WindowNative.GetWindowHandle(this));
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
        var scale = WindowScale.OfMonitor(monitor);
        var work = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest).WorkArea;
        var placed = QuickAddPlacement.Centre(
            new PixelRect(work.X, work.Y, work.Width, work.Height), CardWidth, heightDips, scale);
        // The card's size is its client area: add whatever frame the window still has.
        var frameWidth = AppWindow.Size.Width - AppWindow.ClientSize.Width;
        var frameHeight = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        AppWindow.MoveAndResize(new RectInt32(
            placed.Left - frameWidth / 2, placed.Top - frameHeight / 2,
            placed.Width + frameWidth, placed.Height + frameHeight));
    }

    // Like the WPF card: no frame at all. A borderless OverlappedPresenter still gives the window a 3 px dialog
    // frame (WS_DLGFRAME, which it restores when the style is changed), and Windows 11 draws its 1 px window border
    // over that. The window procedure gives the whole window to the client area instead, and the border is turned
    // off; the rounded corners stay, matching the card's 8 px overlay radius.
    private static unsafe void DropSystemFrame(nint hwnd)
    {
        SetWindowSubclass(hwnd, &NoFrameProc, NoFrameSubclassId, 0);
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate);
        var none = DwmColorNone;
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref none, sizeof(uint));
    }

    [UnmanagedCallersOnly]
    private static unsafe nint NoFrameProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        switch (message)
        {
            // With wParam TRUE, returning 0 leaves the proposed window rectangle as the client rectangle.
            case WmNcCalcSize when wParam != 0:
                return 0;
            case WmNcDestroy:
                RemoveWindowSubclass(hwnd, &NoFrameProc, NoFrameSubclassId);
                break;
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private const uint MonitorDefaultToNearest = 2;
    private const uint WmNcCalcSize = 0x0083;
    private const uint WmNcDestroy = 0x0082;
    private const nuint NoFrameSubclassId = 1;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    /// <summary>DWMWA_BORDER_COLOR: the colour of the Windows 11 window border.</summary>
    private const int DwmwaBorderColor = 34;

    /// <summary>DWMWA_COLOR_NONE: no window border.</summary>
    private const uint DwmColorNone = 0xFFFFFFFE;

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

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetWindowSubclass(
        nint hwnd, delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint data);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool RemoveWindowSubclass(
        nint hwnd, delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref uint value, int size);
}
