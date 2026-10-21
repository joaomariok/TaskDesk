using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation.Tests;

/// <summary>Reminders over in-memory Tasks; the due rule is the Core one, evaluated at the fake clock.</summary>
internal sealed class FakeReminderService(FakeTimeProvider clock, List<TaskItem>? tasks = null) : IReminderService
{
    public List<TaskItem> Tasks { get; } = tasks ?? [];

    /// <summary>Every Reminder change (add, update, remove): the Task it changed and the Reminder's new instant.</summary>
    public List<(Guid TaskId, DateTime? AtUtc)> ReminderChanges { get; } = [];

    public int ListCalls { get; private set; }

    /// <summary>When set, the next ListDueAsync throws it.</summary>
    public Exception? FailNextList { get; set; }

    public TaskItem Add(string title, DateTime? reminderUtc)
    {
        var task = new TaskItem { Id = Guid.CreateVersion7(), Title = title };
        if (reminderUtc is { } at)
        {
            task.Reminders.Add(new Reminder { Id = Guid.CreateVersion7(), TaskId = task.Id, AtUtc = at });
        }

        Tasks.Add(task);
        return task;
    }

    /// <summary>When set, each Reminder change waits for it before it stores anything.</summary>
    public TaskCompletionSource? HoldChanges { get; set; }

    public Task<TaskItem> AddReminderAsync(
        Guid taskId, ReminderValue reminder, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            Tasks.SingleOrDefault(t => t.Id == taskId)
                ?? throw new KeyNotFoundException($"Task {taskId} does not exist."),
            reminder.AtUtc,
            task => ReminderCalculator.AddReminder(task, reminder, clock.GetUtcNow()));

    public Task<TaskItem> UpdateReminderAsync(
        Guid reminderId, ReminderValue reminder, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            Owner(reminderId), reminder.AtUtc,
            task => ReminderCalculator.UpdateReminder(task, reminderId, reminder, clock.GetUtcNow()));

    public Task<TaskItem> RemoveReminderAsync(Guid reminderId, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            Owner(reminderId), null, task => ReminderCalculator.RemoveReminder(task, reminderId, clock.GetUtcNow()));

    public Task<TaskItem> SetNotifyAtDueAsync(
        Guid taskId, bool notifyAtDue, CancellationToken cancellationToken = default)
    {
        var task = Tasks.SingleOrDefault(t => t.Id == taskId)
            ?? throw new KeyNotFoundException($"Task {taskId} does not exist.");
        ReminderCalculator.SetNotifyAtDue(task, notifyAtDue, clock.GetUtcNow());
        return Task.FromResult(task);
    }

    public Task<TaskItem> SetNagPeriodAsync(Guid taskId, NagPeriod? period, CancellationToken cancellationToken = default)
    {
        var task = Tasks.SingleOrDefault(t => t.Id == taskId)
            ?? throw new KeyNotFoundException($"Task {taskId} does not exist.");
        ReminderCalculator.SetNagPeriod(task, period, clock.GetUtcNow());
        return Task.FromResult(task);
    }

    private TaskItem Owner(Guid reminderId) =>
        Tasks.SingleOrDefault(t => t.Reminders.Any(r => r.Id == reminderId))
        ?? throw new KeyNotFoundException($"Reminder {reminderId} does not exist.");

    private async Task<TaskItem> ChangeAsync(TaskItem task, DateTime? atUtc, Action<TaskItem> change)
    {
        ReminderChanges.Add((task.Id, atUtc));
        if (HoldChanges is { } hold)
        {
            await hold.Task;
        }

        change(task);
        return task;
    }

    /// <summary>Tasks whose Reminder TryMarkFiredAsync refuses, as if another check claimed it after listing.</summary>
    public HashSet<Guid> RefuseClaims { get; } = [];

    public List<(Guid TaskId, TimeSpan Duration)> SnoozeCalls { get; } = [];
    public List<Guid> ClearSnoozeCalls { get; } = [];

    public Task<TaskItem> SnoozeAsync(Guid taskId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        SnoozeCalls.Add((taskId, duration));
        var task = Tasks.SingleOrDefault(t => t.Id == taskId)
            ?? throw new KeyNotFoundException($"Task {taskId} does not exist.");
        ReminderCalculator.Snooze(task, clock.GetUtcNow(), duration);
        return Task.FromResult(task);
    }

    public Task<TaskItem> ClearSnoozeAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        ClearSnoozeCalls.Add(taskId);
        var task = Tasks.Single(t => t.Id == taskId);
        ReminderCalculator.ClearSnooze(task);
        return Task.FromResult(task);
    }

    public Task<IReadOnlyList<DueReminder>> ListDueAsync(CancellationToken cancellationToken = default)
    {
        ListCalls++;
        if (FailNextList is { } failure)
        {
            FailNextList = null;
            throw failure;
        }

        var now = clock.GetUtcNow();
        IReadOnlyList<DueReminder> due = [.. ReminderCalculator.SelectDue(Tasks, now)
            .Select(t => new DueReminder(
                t.Id, t.Title, t.DueDate, t.DueTime, "Inbox", ReminderCalculator.DueInstant(t, now)!.Value))];
        return Task.FromResult(due);
    }

    public async Task<bool> TryMarkFiredAsync(Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default) =>
        await TryClaimAsync(taskId, dueAtUtc, cancellationToken) is not null;

    // "Notified through" before each live claim, so releasing it restores exactly that value.
    private readonly Dictionary<Guid, DateTime?> _beforeClaim = [];

    public Task<DateTime?> TryClaimAsync(Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default)
    {
        var task = Tasks.Single(t => t.Id == taskId);
        if (RefuseClaims.Contains(taskId) || ReminderCalculator.DueInstant(task, clock.GetUtcNow()) != dueAtUtc)
        {
            return Task.FromResult<DateTime?>(null);
        }

        _beforeClaim[taskId] = task.NotifiedThroughUtc;
        task.NotifiedThroughUtc = dueAtUtc;
        return Task.FromResult(task.NotifiedThroughUtc);
    }

    /// <summary>The Tasks whose claim was released, in order.</summary>
    public List<Guid> Released { get; } = [];

    public Task<bool> ReleaseClaimAsync(Guid taskId, DateTime firedUtc, CancellationToken cancellationToken = default)
    {
        var task = Tasks.Single(t => t.Id == taskId);
        if (task.NotifiedThroughUtc != firedUtc || !_beforeClaim.Remove(taskId, out var before))
        {
            return Task.FromResult(false);
        }

        task.NotifiedThroughUtc = before;
        Released.Add(taskId);
        return Task.FromResult(true);
    }
}

/// <summary>Records what would be shown; never talks to Windows.</summary>
internal sealed class FakeNotificationService : INotificationService
{
    public List<ReminderNotification> Shown { get; } = [];
    public List<MissedRemindersSummary> Summaries { get; } = [];
    public int RegisterCalls { get; private set; }
    public int UnregisterCalls { get; private set; }

    /// <summary>When set, Register throws it.</summary>
    public Exception? RegisterFailure { get; set; }

    /// <summary>When set, Show throws it.</summary>
    public Exception? ShowFailure { get; set; }

    /// <summary>Show throws for the Reminders of these Tasks.</summary>
    public HashSet<Guid> FailFor { get; } = [];

    public NotificationActivation? LaunchActivation { get; set; }

    public event Action<NotificationActivation>? Activated;

    public void Register()
    {
        RegisterCalls++;
        if (RegisterFailure is { } failure)
        {
            throw failure;
        }
    }

    public NotificationActivation? GetLaunchActivation() => LaunchActivation;

    public void Show(ReminderNotification notification)
    {
        if (ShowFailure is { } failure)
        {
            throw failure;
        }

        if (FailFor.Contains(notification.TaskId))
        {
            throw new InvalidOperationException($"Showing {notification.TaskId} failed.");
        }

        Shown.Add(notification);
    }

    public void ShowSummary(MissedRemindersSummary summary)
    {
        if (ShowFailure is { } failure)
        {
            throw failure;
        }

        Summaries.Add(summary);
    }

    public void Unregister() => UnregisterCalls++;

    public void Click(NotificationActivation activation) => Activated?.Invoke(activation);
}

internal sealed class FakeTaskChanges : ITaskChangeNotifier
{
    public event Action? TasksChanged;

    public bool HasListeners => TasksChanged is not null;

    public void Raise() => TasksChanged?.Invoke();
}
