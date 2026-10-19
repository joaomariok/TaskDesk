namespace TaskDesk.Core.Shell;

/// <summary>
/// The installed app's updates (ADR 0005). The only implementation wraps Velopack's update manager over the public
/// GitHub Releases of TaskDesk; tests use a fake.
/// </summary>
public interface IAppUpdates
{
    /// <summary>True when this exe runs from a Velopack install; false for a dev build or a copied folder.</summary>
    bool IsInstalled { get; }

    /// <summary>The running version, such as "1.2.3".</summary>
    string CurrentVersion { get; }

    /// <summary>
    /// Checks for a newer release and downloads it. Returns the version ready to apply, or null when there is none.
    /// Only valid while <see cref="IsInstalled"/>. Failures (offline, GitHub down) throw.
    /// </summary>
    Task<string?> CheckAndDownloadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The version a check (of this run or an earlier one) downloaded and that is waiting to be applied; null when
    /// none. Reads the local packages folder only. Only valid while <see cref="IsInstalled"/>.
    /// </summary>
    string? DownloadedVersion { get; }

    /// <summary>
    /// Ends this process at once, applies the downloaded update and starts the new version with
    /// <paramref name="restartArguments"/>. Run the app's exit path first: nothing after this call runs.
    /// </summary>
    void RestartToApply(IReadOnlyList<string> restartArguments);

    /// <summary>
    /// Starts the running version again with <paramref name="restartArguments"/> and returns, leaving the downloaded
    /// update pending; the caller then shuts this process down. Call it once this process has released the
    /// single-instance guard, so the new process becomes the running copy.
    /// </summary>
    void RestartWithoutApplying(IReadOnlyList<string> restartArguments);
}
