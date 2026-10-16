using System.Windows;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Shell;

/// <summary>
/// The only place that touches the experimental <c>Application.ThemeMode</c> API (WPF0001),
/// so it is easy to replace if the API changes.
/// </summary>
internal sealed class WpfThemeService : IThemeService
{
    public void Apply(ThemeChoice theme)
    {
        var mode = theme switch
        {
            ThemeChoice.Light => ThemeMode.Light,
            ThemeChoice.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };

        Application.Current.Dispatcher.Invoke(() => Application.Current.ThemeMode = mode);
    }
}
