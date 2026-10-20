using TaskDesk.Core.Paths;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation;

/// <summary>
/// The decision made before the host exists, in this order: a launch that finds another copy running forwards to it
/// and ends; the single-instance owner applies an update downloaded by an earlier run and restarts (ADR 0005);
/// otherwise start-up carries on, holding the guard. Velopack's own apply-on-start is off: it would run in every
/// process, before the single-instance check, and stop a running copy without its exit path.
/// </summary>
public static class LaunchGate
{
    /// <summary>
    /// Claims <paramref name="singleInstance"/>. When another copy holds it, signals that copy with the launch's
    /// <c>--minimized</c> flag and <paramref name="launchActivation"/>, releases the guard and returns
    /// <see cref="LaunchOutcome.ForwardAndEnd"/>; nothing is applied. Otherwise applies a downloaded update (see
    /// <see cref="ApplyIfPending"/>; a non-null <paramref name="launchActivation"/> is a launch by a toast) and
    /// returns <see cref="LaunchOutcome.ApplyAndRestart"/>, or <see cref="LaunchOutcome.CarryOn"/> with the guard
    /// held. A failed apply carries on, with the failure in <see cref="LaunchDecision.ApplyFailure"/> for the App to
    /// log once its logger exists.
    /// </summary>
    public static async Task<LaunchDecision> DecideAsync(
        ISingleInstance singleInstance,
        IAppUpdates updates,
        RuntimeMode mode,
        IReadOnlyList<string> launchArguments,
        NotificationActivation? launchActivation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(singleInstance);
        ArgumentNullException.ThrowIfNull(launchArguments);
        if (!singleInstance.TryAcquire())
        {
            var minimized = launchArguments.Contains(SecondLaunchArguments.MinimizedFlag, StringComparer.OrdinalIgnoreCase);
            await singleInstance.SignalExistingAsync(
                SecondLaunchArguments.Build(minimized, launchActivation), cancellationToken);
            singleInstance.Dispose();
            return new LaunchDecision(LaunchOutcome.ForwardAndEnd);
        }

        try
        {
            return ApplyIfPending(updates, mode, launchActivation is not null, launchArguments)
                ? new LaunchDecision(LaunchOutcome.ApplyAndRestart)
                : new LaunchDecision(LaunchOutcome.CarryOn);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new LaunchDecision(LaunchOutcome.CarryOn, ex);
        }
    }

    /// <summary>
    /// Marks a process that the updater started. Velopack's updater restarts the app with the given arguments after
    /// applying, and also when applying failed (a locked file, a corrupt package), which leaves the update pending:
    /// a process with this flag never applies at start, or it would try again at every start. The rest of start-up
    /// ignores it, and it is never written to the Run key or forwarded to a running copy, so a later normal start
    /// (logon, the Start menu) still applies. Restart to update also passes it when it starts the current version
    /// again without applying (<see cref="ShellController.RestartToUpdateAsync"/>).
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

/// <summary>What the App does after <see cref="LaunchGate.DecideAsync"/>.</summary>
public enum LaunchOutcome
{
    /// <summary>Another copy runs and has been signalled: end this process.</summary>
    ForwardAndEnd,

    /// <summary>The downloaded update is being applied: end this process (in the app the apply ends it).</summary>
    ApplyAndRestart,

    /// <summary>This process owns the instance: build the host and start.</summary>
    CarryOn,
}

/// <summary>The launch gate's outcome, with the failure of an apply tried at start, if any.</summary>
public sealed record LaunchDecision(LaunchOutcome Outcome, Exception? ApplyFailure = null);
