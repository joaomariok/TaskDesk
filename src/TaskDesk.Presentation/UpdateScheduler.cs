using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation;

/// <summary>
/// Keeps an installed TaskDesk up to date (ADR 0005). Checks GitHub Releases at start and every
/// <see cref="Interval"/>, on the injected <see cref="TimeProvider"/>, and downloads a newer version silently;
/// it is installed at the next start (<see cref="StartupUpdate"/>) or by Restart to update. Does nothing when the
/// app is not installed (a dev build or a copied folder) or the runtime mode turns updates off (a redirected data
/// folder). Checks never overlap: a tick while one
/// runs is skipped. Every failure (offline, GitHub down) is logged and swallowed, never shown; the next interval
/// retries. The downloaded version is <see cref="PendingVersion"/>.
/// </summary>
public sealed class UpdateScheduler(
    IAppUpdates updates,
    RuntimeMode mode,
    TimeProvider clock,
    ILogger<UpdateScheduler>? logger = null) : IDisposable
{
    /// <summary>How often the scheduler checks for a new release.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>How long <see cref="StopAsync"/> waits for a cancelled check to end.</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger _logger = logger ?? NullLogger<UpdateScheduler>.Instance;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private ITimer? _timer;
    private bool _running;
    private bool _disposed;
    private string? _pendingVersion;

    /// <summary>
    /// Raised when <see cref="PendingVersion"/> changes, on the thread of the check that found it (not the UI
    /// thread).
    /// </summary>
    public event Action? PendingVersionChanged;

    /// <summary>The downloaded version that is applied at the next start or by a restart; null when none.</summary>
    public string? PendingVersion
    {
        get
        {
            lock (_gate)
            {
                return _pendingVersion;
            }
        }
    }

    /// <summary>Completes when the check in progress has finished. Never faults.</summary>
    public Task CurrentCheck { get; private set; } = Task.CompletedTask;

    /// <summary>Checks now, then every <see cref="Interval"/>; or, when updates are off, logs why and does nothing.</summary>
    public void Start()
    {
        if (!mode.ManagesUpdates)
        {
            _logger.LogInformation("Update checks off: runtime mode {Mode}", mode);
            return;
        }

        if (!updates.IsInstalled)
        {
            _logger.LogInformation("Update checks off: version {Version} is not an installed copy", updates.CurrentVersion);
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _timer = clock.CreateTimer(_ => RequestCheck(), null, Interval, Interval);
        }

        RequestCheck();
    }

    /// <summary>
    /// Stops the checks (see <see cref="Dispose"/>), then waits up to <see cref="StopTimeout"/> for the cancelled
    /// check to end, so a restart into the update never applies while a download still writes to, or cleans, the
    /// packages folder. Past the timeout it logs a warning and returns. Never throws.
    /// </summary>
    public async Task StopAsync()
    {
        Dispose();
        try
        {
            await CurrentCheck.WaitAsync(StopTimeout, clock).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("The update check did not end within {Timeout}; exiting without waiting", StopTimeout);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        // Ends a running download; the CancellationTokenSource is not disposed, since that check may still read it.
        _stop.Cancel();
    }

    private void RequestCheck()
    {
        lock (_gate)
        {
            if (_disposed || _running)
            {
                return;
            }

            _running = true;
            CurrentCheck = Task.Run(CheckOnceAsync);
        }
    }

    private async Task CheckOnceAsync()
    {
        try
        {
            if (await updates.CheckAndDownloadAsync(_stop.Token) is { } found)
            {
                SetPending(found);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // Disposed during the check: the app is exiting.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Update check failed; retrying in {Interval}", Interval);
        }
        finally
        {
            lock (_gate)
            {
                _running = false;
            }
        }
    }

    private void SetPending(string version)
    {
        lock (_gate)
        {
            if (_disposed || _pendingVersion == version)
            {
                return;
            }

            _pendingVersion = version;
        }

        _logger.LogInformation("Update {Version} downloaded; it is installed at the next start", version);
        PendingVersionChanged?.Invoke();
    }
}
