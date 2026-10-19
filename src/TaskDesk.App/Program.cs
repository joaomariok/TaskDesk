using Microsoft.UI.Dispatching;
using Velopack;

namespace TaskDesk.App;

/// <summary>
/// The App's entry point, in place of the one WinUI generates (DISABLE_XAML_GENERATED_MAIN). Velopack runs first:
/// the installer and updater launch this exe with hook arguments and expect it to handle them and exit at once,
/// before any single-instance, host or window code. Then it starts WinUI as the generated entry point did.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }
}
