using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TaskDesk.Core.Shell;
using TaskDesk.Views;

namespace TaskDesk.App.Shell;

/// <summary>Adapts the WinUI main window to <see cref="IMainWindow"/>, marshalling to its UI thread.</summary>
internal sealed partial class WinUiMainWindow(MainWindow window) : IMainWindow, IAppShutdown
{
    public bool IsVisible => UiThread.Invoke(window.DispatcherQueue, () => window.AppWindow.IsVisible);

    public void ShowAndFocus() => UiThread.Invoke(window.DispatcherQueue, () =>
    {
        window.AppWindow.Show();
        if (window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        window.Activate();
        // A second launch has granted this process the foreground (AllowSetForegroundWindow).
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(window));
    });

    public void Hide() => UiThread.Invoke(window.DispatcherQueue, window.AppWindow.Hide);

    public void Shutdown() => UiThread.Invoke(window.DispatcherQueue, Application.Current.Exit);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);
}
