using Microsoft.UI.Dispatching;
using TaskDesk.App.Shell;
using Velopack;

namespace TaskDesk.App;

/// <summary>
/// The App's entry point, in place of the one WinUI generates (DISABLE_XAML_GENERATED_MAIN). Velopack runs first:
/// the installer and updater launch this exe with hook arguments and expect it to handle them and exit at once,
/// before any single-instance, host or window code. The uninstall hook removes what the app registered (#90).
/// Velopack's own apply-on-start is always off: it would apply a downloaded update in every process, before the
/// single-instance check, and stop a running copy without its exit path. The App applies it instead, in the
/// single-instance owner only (<see cref="Presentation.StartupUpdate"/>).
/// Then it starts WinUI as the generated entry point did.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(VelopackInstall.OnBeforeUninstall)
            .SetAutoApplyOnStartup(false)
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
