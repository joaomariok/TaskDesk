using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace TaskDesk.Views;

/// <summary>
/// Display scale (1.0 at 100 %, 1.25 at 125 %) of a window or a monitor, for converting DIPs to the physical
/// pixels <see cref="Microsoft.UI.Windowing.AppWindow"/> works in. When Windows reports no DPI the scale is 1.0.
/// </summary>
public static partial class WindowScale
{
    public static double Of(Window window) => OfWindowHandle(WindowNative.GetWindowHandle(window));

    public static double OfWindowHandle(nint hwnd) => FromDpi(GetDpiForWindow(hwnd));

    /// <summary>The effective scale of a monitor (HMONITOR).</summary>
    public static double OfMonitor(nint monitor) =>
        GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 ? FromDpi(dpiX) : 1.0;

    /// <summary>
    /// The system scale: the primary monitor's at sign-in, which a system-DPI-aware process (the WPF app) worked at.
    /// </summary>
    public static double OfSystem() => FromDpi(GetDpiForSystem());

    private static double FromDpi(uint dpi) => dpi > 0 ? dpi / 96.0 : 1.0;

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    private const int MdtEffectiveDpi = 0;

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
}
