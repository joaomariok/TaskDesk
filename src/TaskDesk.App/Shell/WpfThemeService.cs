using System.Windows;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Shell;

/// <summary>
/// The only code that touches the experimental <c>Application.ThemeMode</c> API (WPF0001), so it is
/// easy to replace if the API changes. App.xaml also sets it, to System, so Fluent's styles exist
/// before the app's own styles are loaded.
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
