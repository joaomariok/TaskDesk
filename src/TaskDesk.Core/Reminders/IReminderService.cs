using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Reminders;

/// <summary>Use cases for the Reminder of a Task and for finding the Reminders that are due.</summary>
public interface IReminderService
{
    /// <summary>
    /// Sets or clears the planned Reminder of a Task. A change clears the Snooze and counts moments already
    /// past as notified, so a Reminder moved later re-arms and one set in the past never fires (see
    /// <see cref="ReminderCalculator.SetPlanned"/>). Returns the Task with its Tags and Subtasks loaded.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Task has this id.</exception>
    Task<TaskItem> SetReminderAsync(Guid taskId, DateTime? reminderUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Snoozes a Task until now + <paramref name="duration"/>: it notifies once when the Snooze ends; the planned
    /// Reminder is left unchanged (see <see cref="ReminderCalculator.Snooze"/>). A completed Task is left as it
    /// is. Returns the Task with its Tags and Subtasks loaded.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Task has this id.</exception>
    Task<TaskItem> SnoozeAsync(Guid taskId, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the Snooze of a Task, which then notifies once at the next check for its latest moment already
    /// past (see <see cref="ReminderCalculator.ClearSnooze"/>). Returns the Task with its Tags and Subtasks loaded.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Task has this id.</exception>
    Task<TaskItem> ClearSnoozeAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The Tasks due to notify now (see <see cref="ReminderCalculator.DueInstant"/>), one entry per Task with its
    /// due instant, the earliest due instant first.
    /// </summary>
    Task<IReadOnlyList<DueReminder>> ListDueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims a listed due Task (see <see cref="TryClaimAsync"/>), so it notifies once. Returns false, changing
    /// nothing, when it was not claimed.
    /// </summary>
    Task<bool> TryMarkFiredAsync(Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims a listed due Task with one conditional update that advances its "notified through" to
    /// <paramref name="dueAtUtc"/>, its listed due instant. Returns that instant, which identifies this claim for
    /// <see cref="ReleaseClaimAsync"/>; null, changing nothing, when the instant is not reached yet or the Task
    /// was completed, snoozed, edited or claimed since it was listed.
    /// </summary>
    Task<DateTime?> TryClaimAsync(Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes a claim whose notification could not be shown, so a later check retries it: the claimed due
    /// instant is due again, only while "notified through" is still <paramref name="firedUtc"/>. Returns false,
    /// changing nothing, when the Task was re-armed or claimed again since.
    /// </summary>
    Task<bool> ReleaseClaimAsync(Guid taskId, DateTime firedUtc, CancellationToken cancellationToken = default);
}

/// <summary>Tells listeners that Tasks were saved (added, edited, completed, deleted or restored).</summary>
public interface ITaskChangeNotifier
{
    /// <summary>Raised after a save that changed Tasks; may be raised on any thread.</summary>
    event Action? TasksChanged;
}
