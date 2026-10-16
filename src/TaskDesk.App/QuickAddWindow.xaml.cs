using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TaskDesk.Presentation;

namespace TaskDesk.App;

/// <summary>Borderless top-most quick-add window, centred on the monitor that holds the mouse cursor.</summary>
public partial class QuickAddWindow : Window
{
    private readonly QuickAddViewModel _viewModel;
    private bool _closing;

    public QuickAddWindow(QuickAddViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        viewModel.CloseRequested += CloseOnce;
        SourceInitialized += (_, _) => CenterOnCursorMonitor();
        Loaded += (_, _) =>
        {
            Activate();
            TitleBox.Focus();
        };
        Deactivated += (_, _) => _viewModel.CancelCommand.Execute(null);
    }

    private void CloseOnce()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _viewModel.CloseRequested -= CloseOnce;
        Close();
    }

    private void CenterOnCursorMonitor()
    {
        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var scale = GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
        Measure(new Size(Width, double.PositiveInfinity));
        var widthPx = (int)Math.Round(Width * scale);
        var heightPx = (int)Math.Round(DesiredSize.Height * scale);
        var left = info.Work.Left + ((info.Work.Right - info.Work.Left - widthPx) / 2);
        var top = info.Work.Top + ((info.Work.Bottom - info.Work.Top - heightPx) / 2);
        SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, left, top, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
