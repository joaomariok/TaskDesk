using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TaskDesk.Core.Shell;

namespace TaskDesk.Views;

/// <summary>
/// Applies the theme setting to every open window, live: the window's root element takes the theme (its
/// <c>ThemeResource</c> references follow) and so does its standard title bar. System follows the Windows app
/// mode. WinUI keeps no list of open windows, so each window registers itself here once it is created.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private readonly List<Window> _windows = [];
    private ThemeChoice _theme = ThemeChoice.System;

    /// <summary>Gives <paramref name="window"/> the current theme now and on every later switch, until it closes.</summary>
    public void Register(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_windows)
        {
            _windows.Add(window);
        }

        window.Closed += (_, _) =>
        {
            lock (_windows)
            {
                _windows.Remove(window);
            }
        };
        OnUiThread(window, () => ApplyTo(window, _theme));
    }

    public void Apply(ThemeChoice theme)
    {
        Window[] windows;
        lock (_windows)
        {
            _theme = theme;
            windows = [.. _windows];
        }

        foreach (var window in windows)
        {
            OnUiThread(window, () => ApplyTo(window, theme));
        }
    }

    private static void ApplyTo(Window window, ThemeChoice theme)
    {
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                ThemeChoice.Light => ElementTheme.Light,
                ThemeChoice.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        window.AppWindow.TitleBar.PreferredTheme = theme switch
        {
            ThemeChoice.Light => TitleBarTheme.Light,
            ThemeChoice.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    private static void OnUiThread(Window window, Action action)
    {
        if (window.DispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            window.DispatcherQueue.TryEnqueue(() => action());
        }
    }
}
