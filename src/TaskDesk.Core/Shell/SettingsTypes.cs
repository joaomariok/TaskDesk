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

/// <summary>Puts text on the system clipboard.</summary>
public interface IClipboardService
{
    void SetText(string text);
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
    public WindowLayout FitTo(double screenLeft, double screenTop, double screenWidth, double screenHeight) =>
        WindowLayoutCalculator.Fit(this, screenLeft, screenTop, screenWidth, screenHeight);
}
