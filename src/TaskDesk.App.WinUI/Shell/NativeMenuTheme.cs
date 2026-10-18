using System.Runtime.InteropServices;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Shell;

/// <summary>
/// Themes the menus Windows draws for the process, such as the tray icon's native popup menu (H.NotifyIcon.WinUI's
/// PopupMenu mode), which XAML's theme never reaches. Windows offers no documented API for this; uxtheme's
/// SetPreferredAppMode (ordinal 135) and FlushMenuThemes (ordinal 136), present since Windows 10 1903, are what
/// Explorer and other Win32 apps use. Dark forces dark menus, Light forces light ones and System follows the
/// Windows app mode, as the WPF app's tray menu did.
/// </summary>
internal static partial class NativeMenuTheme
{
    /// <summary>uxtheme's PreferredAppMode.</summary>
    internal enum AppMode
    {
        Default = 0,
        AllowDark = 1,
        ForceDark = 2,
        ForceLight = 3,
    }

    public static AppMode ModeFor(ThemeChoice theme) => theme switch
    {
        ThemeChoice.Dark => AppMode.ForceDark,
        ThemeChoice.Light => AppMode.ForceLight,
        _ => AppMode.AllowDark,
    };

    /// <summary>Gives menus opened from now on the theme's look.</summary>
    public static void Apply(ThemeChoice theme)
    {
        SetPreferredAppMode(ModeFor(theme));
        FlushMenuThemes();
    }

    [LibraryImport("uxtheme.dll", EntryPoint = "#135")]
    private static partial AppMode SetPreferredAppMode(AppMode mode);

    [LibraryImport("uxtheme.dll", EntryPoint = "#136")]
    private static partial void FlushMenuThemes();
}
