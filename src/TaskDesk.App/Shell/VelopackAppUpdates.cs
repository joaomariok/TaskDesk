using System.Diagnostics;
using System.Reflection;
using TaskDesk.Core.Shell;
using Velopack;
using Velopack.Sources;

namespace TaskDesk.App.Shell;

/// <summary>
/// <see cref="IAppUpdates"/> over Velopack's update manager, reading the public GitHub Releases of TaskDesk with no
/// token (ADR 0005). Covered by manual checks only.
/// </summary>
internal sealed class VelopackAppUpdates : IAppUpdates
{
    /// <summary>The repository whose GitHub Releases the release workflow publishes to.</summary>
    public const string RepositoryUrl = "https://github.com/joaomariok/TaskDesk";

    // Created on first use, so a run that is not installed never builds one.
    private readonly Lazy<UpdateManager> _manager =
        new(() => new UpdateManager(new GithubSource(RepositoryUrl, null, false)));

    public bool IsInstalled => VelopackInstall.IsInstalled;

    /// <summary>The app's version (the pack script publishes with the package's version).</summary>
    public string CurrentVersion { get; } =
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? "0.0.0").Split('+')[0];

    public async Task<string?> CheckAndDownloadAsync(CancellationToken cancellationToken)
    {
        var manager = _manager.Value;
        var update = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (update is null)
        {
            return null;
        }

        var target = update.TargetFullRelease;
        // Downloaded by an earlier check of this run: nothing more to fetch.
        if (manager.UpdatePendingRestart?.Version != target.Version)
        {
            await manager.DownloadUpdatesAsync(update, null, cancellationToken).ConfigureAwait(false);
        }

        return target.Version.ToString();
    }

    public string? DownloadedVersion => _manager.Value.UpdatePendingRestart?.Version.ToString();

    public void RestartToApply(IReadOnlyList<string> restartArguments)
    {
        var manager = _manager.Value;
        manager.ApplyUpdatesAndRestart(manager.UpdatePendingRestart, [.. restartArguments]);
    }

    public void RestartWithoutApplying(IReadOnlyList<string> restartArguments)
    {
        var start = new ProcessStartInfo(
            Environment.ProcessPath ?? throw new InvalidOperationException("No process path."), restartArguments)
        {
            UseShellExecute = false,
        };
        using var _ = Process.Start(start);
    }
}
