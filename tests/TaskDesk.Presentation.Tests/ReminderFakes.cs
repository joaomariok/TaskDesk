using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation.Tests;

/// <summary>Reminders over in-memory Tasks; the due rule is the Core one, evaluated at the fake clock.</summary>
internal sealed class FakeReminderService(FakeTimeProvider clock, List<TaskItem>? tasks = null) : IReminderService
{
    public List<TaskItem> Tasks { get; } = tasks ?? [];
    public List<(Guid TaskId, DateTime? ReminderUtc)> SetCalls { get; } = [];
    public int ListCalls { get; private set; }

    /// <summary>When set, the next ListDueAsync throws it.</summary>
    public Exception? FailNextList { get; set; }

    public TaskItem Add(string title, DateTime? reminderUtc)
    {
        var task = new TaskItem { Id = Guid.CreateVersion7(), Title = title, ReminderUtc = reminderUtc };
        Tasks.Add(task);
        return task;
    }

    public Task<TaskItem> SetReminderAsync(Guid taskId, DateTime? reminderUtc, CancellationToken cancellationToken = default)
    {
        SetCalls.Add((taskId, reminderUtc));
        var task = Tasks.Single(t => t.Id == taskId);
        ReminderCalculator.SetPlanned(task, reminderUtc);
        return Task.FromResult(task);
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

        IReadOnlyList<DueReminder> due = [.. ReminderCalculator.SelectDue(Tasks, clock.GetUtcNow())
            .Select(t => new DueReminder(t.Id, t.Title, t.DueDate, t.DueTime, "Inbox", ReminderCalculator.EffectiveUtc(t)!.Value))];
        return Task.FromResult(due);
    }

    public async Task<bool> TryMarkFiredAsync(Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default) =>
        await TryClaimAsync(taskId, dueAtUtc, cancellationToken) is not null;

    public Task<DateTime?> TryClaimAsync(Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default)
    {
        var task = Tasks.Single(t => t.Id == taskId);
        if (RefuseClaims.Contains(taskId)
            || !ReminderCalculator.IsDue(task, clock.GetUtcNow()) || ReminderCalculator.EffectiveUtc(task) != dueAtUtc)
        {
            return Task.FromResult<DateTime?>(null);
        }

        task.ReminderFiredUtc = clock.GetUtcNow().UtcDateTime;
        return Task.FromResult(task.ReminderFiredUtc);
    }

    /// <summary>The Tasks whose claim was released, in order.</summary>
    public List<Guid> Released { get; } = [];

    public Task<bool> ReleaseClaimAsync(Guid taskId, DateTime firedUtc, CancellationToken cancellationToken = default)
    {
        var task = Tasks.Single(t => t.Id == taskId);
        if (task.ReminderFiredUtc != firedUtc)
        {
            return Task.FromResult(false);
        }

        task.ReminderFiredUtc = null;
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
