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
    /// Marks a process that the updater started. Velopack's updater restarts the app with the given arguments after
    /// applying, and also when applying failed (a locked file, a corrupt package), which leaves the update pending:
    /// a process with this flag never applies at start, or it would try again at every start. The rest of start-up
    /// ignores it, and it is never written to the Run key or forwarded to a running copy, so a later normal start
    /// (logon, the Start menu) still applies.
    /// </summary>
    public const string RestartedFlag = "--restarted-by-updater";

    /// <summary>
    /// The arguments the updater restarts the app with: <paramref name="launchArguments"/>, then
    /// <see cref="RestartedFlag"/>.
    /// </summary>
    public static IReadOnlyList<string> RestartArguments(IReadOnlyList<string> launchArguments) =>
        [.. launchArguments, RestartedFlag];

    /// <summary>
    /// Applies the downloaded update and restarts with <paramref name="launchArguments"/> (see
    /// <see cref="RestartArguments"/>), so a logon start stays minimized. Returns false, leaving the update pending,
    /// when the runtime mode turns updates off, the copy is not installed, nothing is downloaded, the updater started
    /// this process (<see cref="RestartedFlag"/>), or a notification launched it (the restarted one would lose the
    /// toast's action). The update then applies at the next normal start. In the app a successful apply never
    /// returns.
    /// </summary>
    public static bool ApplyIfPending(
        IAppUpdates updates,
        RuntimeMode mode,
        bool launchedByNotification,
        IReadOnlyList<string> launchArguments)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(mode);
        if (!mode.ManagesUpdates
            || launchedByNotification
            || launchArguments.Contains(RestartedFlag, StringComparer.OrdinalIgnoreCase)
            || !updates.IsInstalled
            || updates.DownloadedVersion is null)
        {
            return false;
        }

        updates.RestartToApply(RestartArguments(launchArguments));
        return true;
    }
}
