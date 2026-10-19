using TaskDesk.Core.Paths;
using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation;

/// <summary>
/// Applies an update downloaded by an earlier run when TaskDesk starts (ADR 0005). Velopack's own apply-on-start is
/// off: it would run in every process, before the single-instance check, and stop a running copy without its exit
/// path. Call this only in the process that owns the single-instance guard, before the host and window are built.
/// </summary>
public static class StartupUpdate
{
    /// <summary>
    /// Applies the downloaded update and restarts with <paramref name="launchArguments"/>, so a logon start stays
    /// minimized. Returns false, leaving the update pending, when the runtime mode turns updates off, the copy is not
    /// installed, nothing is downloaded, or a notification launched the process (the restarted one would lose the
    /// toast's action; the update applies at the next normal start). In the app a successful apply never returns.
    /// </summary>
    public static bool ApplyIfPending(
        IAppUpdates updates,
        RuntimeMode mode,
        bool launchedByNotification,
        IReadOnlyList<string> launchArguments)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(mode);
        if (!mode.ManagesUpdates || launchedByNotification || !updates.IsInstalled || updates.DownloadedVersion is null)
        {
            return false;
        }

        updates.RestartToApply(launchArguments);
        return true;
    }
}
