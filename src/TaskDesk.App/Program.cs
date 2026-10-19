using Microsoft.UI.Dispatching;
using TaskDesk.App.Shell;
using TaskDesk.Core.Paths;
using Velopack;

namespace TaskDesk.App;

/// <summary>
/// The App's entry point, in place of the one WinUI generates (DISABLE_XAML_GENERATED_MAIN). Velopack runs first:
/// the installer and updater launch this exe with hook arguments and expect it to handle them and exit at once,
/// before any single-instance, host or window code. The uninstall hook removes what the app registered (#90).
/// Velopack applies a downloaded update at start only when the runtime mode manages updates, so a sandbox-mode run
/// (a redirected data folder) leaves a pending update pending (#92).
/// Then it starts WinUI as the generated entry point did.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(VelopackInstall.OnBeforeUninstall)
            .SetAutoApplyOnStartup(RuntimeMode.FromEnvironment().ManagesUpdates)
            .Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }
}
