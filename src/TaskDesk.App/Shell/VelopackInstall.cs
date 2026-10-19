using Microsoft.Windows.AppNotifications;
using TaskDesk.Core.Shell;
using Velopack;
using Velopack.Locators;

namespace TaskDesk.App.Shell;

/// <summary>The Velopack install this exe may run from. Covered by manual checks only.</summary>
internal static class VelopackInstall
{
    /// <summary>
    /// True when this exe runs from a Velopack install; false for a Debug build or a publish folder. Read after
    /// <c>VelopackApp.Run</c>, which sets the locator.
    /// </summary>
    public static bool IsInstalled =>
        VelopackLocator.IsCurrentSet && VelopackLocator.Current.CurrentlyInstalledVersion is not null;

    /// <summary>
    /// Velopack's uninstall hook: no UI, and it must finish within Velopack's time limit. It runs in
    /// <c>VelopackApp.Run</c>, before any host, and after Velopack has set this process's AppUserModelId
    /// (<c>velopack.TaskDesk.App</c>), so the unregister-all call removes the registration made under that id.
    /// A failure is logged by Velopack and does not stop the uninstall.
    /// </summary>
    public static void OnBeforeUninstall(SemanticVersion version) => UninstallCleanup.Run(
        new RegistryRunKey(),
        Environment.ProcessPath ?? throw new InvalidOperationException("No process path."),
        () => AppNotificationManager.Default.UnregisterAll());
}
