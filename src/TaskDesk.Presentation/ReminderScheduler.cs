using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaskDesk.Core.Reminders;

namespace TaskDesk.Presentation;

/// <summary>
/// Shows due Reminders (SPEC 7.4). Checks on start, every <see cref="Interval"/>, after every Task change,
/// and whenever <see cref="RequestCheck"/> is called (the App calls it on resume from sleep). A polling loop
/// survives sleep, clock and time-zone changes. Which Reminders are due is the Core rule; each one is
/// claimed (marked fired) before it is shown, so it fires once. Delivery is whatever
/// <see cref="INotificationService"/> is given.
/// </summary>
public sealed class ReminderScheduler(
    IReminderService reminders,
    INotificationService notifications,
    ITaskChangeNotifier taskChanges,
    TimeProvider clock,
    ILogger<ReminderScheduler>? logger = null) : IDisposable
{
    /// <summary>How often the scheduler checks for due Reminders.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger = logger ?? NullLogger<ReminderScheduler>.Instance;
    private readonly Lock _gate = new();
    private ITimer? _timer;
    private bool _running;
    private bool _again;
    private bool _disposed;

    /// <summary>Completes when the check in progress (and any queued after it) has finished.</summary>
    public Task CurrentCheck { get; private set; } = Task.CompletedTask;

    /// <summary>Checks now, then every <see cref="Interval"/> and after every Task change.</summary>
    public Task StartAsync()
    {
        taskChanges.TasksChanged += OnTasksChanged;
        _timer = clock.CreateTimer(_ => RequestCheck(), null, Interval, Interval);
        return RequestCheck();
    }

    /// <summary>
    /// Checks for due Reminders as soon as possible. A request made while a check runs queues one more check
    /// after it. Returns the task that completes when that check has run.
    /// </summary>
    public Task RequestCheck()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return CurrentCheck;
            }

            if (_running)
            {
                _again = true;
                return CurrentCheck;
            }

            _running = true;
            CurrentCheck = Task.Run(RunChecksAsync);
            return CurrentCheck;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        taskChanges.TasksChanged -= OnTasksChanged;
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTasksChanged() => RequestCheck();

    private async Task RunChecksAsync()
    {
        while (true)
        {
            try
            {
                await CheckOnceAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Reminder check failed");
            }

            lock (_gate)
            {
                if (!_again || _disposed)
                {
                    _running = false;
                    return;
                }

                _again = false;
            }
        }
    }

    private async Task CheckOnceAsync()
    {
        foreach (var due in await reminders.ListDueAsync())
        {
            // Claim first: a Reminder edited, completed or claimed since it was listed is not shown.
            if (await reminders.TryMarkFiredAsync(due.TaskId, due.DueAtUtc))
            {
                notifications.Show(ReminderNotification.For(due, CultureInfo.CurrentCulture));
            }
        }
    }
}
