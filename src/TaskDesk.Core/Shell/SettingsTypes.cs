namespace TaskDesk.Core.Shell;

public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

public enum CloseBehavior
{
    HideToTray,
    Exit,
}

/// <summary>Applies the chosen theme to the running application, immediately.</summary>
public interface IThemeService
{
    void Apply(ThemeChoice theme);
}

/// <summary>Opens a folder in the file manager.</summary>
public interface IFolderOpener
{
    void Open(string path);
}

/// <summary>Remembered main-window geometry. <c>PaneWidths</c> is keyed by pane name.</summary>
public sealed record WindowLayout(
    double Left,
    double Top,
    double Width,
    double Height,
    bool Maximized = false,
    IReadOnlyDictionary<string, double>? PaneWidths = null)
{
    public const double MinWidth = 800;
    public const double MinHeight = 500;

    /// <summary>
    /// Returns a layout that respects the minimum size and lies fully inside the given screen area,
    /// so a window saved on a monitor that is gone is still reachable.
    /// </summary>
    public WindowLayout FitTo(double screenLeft, double screenTop, double screenWidth, double screenHeight)
    {
        var width = Math.Max(MinWidth, Math.Min(Width, screenWidth));
        var height = Math.Max(MinHeight, Math.Min(Height, screenHeight));
        var left = Math.Clamp(Left, screenLeft, Math.Max(screenLeft, screenLeft + screenWidth - width));
        var top = Math.Clamp(Top, screenTop, Math.Max(screenTop, screenTop + screenHeight - height));
        return this with { Left = left, Top = top, Width = width, Height = height };
    }
}
