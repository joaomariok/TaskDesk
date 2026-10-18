using Microsoft.UI.Xaml;

namespace TaskDesk.Views.Dialogs;

/// <summary>
/// The window dialogs and file pickers open on: the last activated of the registered windows that are still open
/// (a registered window counts as activated when it registers). WinUI keeps no list of open windows, so each
/// window that can open a dialog registers itself here once it is created. A multi-step dialog flow keeps the
/// window its first step opened on (<see cref="DialogService.ShowOnOwnerAsync{T}"/>).
/// </summary>
public sealed class ActiveWindow
{
    // Most recently activated last.
    private readonly List<Window> _windows = [];

    public Window? Current
    {
        get
        {
            lock (_windows)
            {
                return _windows.Count == 0 ? null : _windows[^1];
            }
        }
    }

    /// <summary>Makes <paramref name="window"/> current now and whenever it is activated, until it closes.</summary>
    public void Register(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        MoveToTop(window);
        window.Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                MoveToTop(window);
            }
        };
        window.Closed += (_, _) =>
        {
            lock (_windows)
            {
                _windows.Remove(window);
            }
        };
    }

    private void MoveToTop(Window window)
    {
        lock (_windows)
        {
            _windows.Remove(window);
            _windows.Add(window);
        }
    }
}
