using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Reminders;

/// <summary>Use cases for the Reminder of a Task and for finding the Reminders that are due.</summary>
public interface IReminderService
{
    /// <summary>
    /// Sets or clears the planned Reminder of a Task. A change re-arms it (see
    /// <see cref="ReminderCalculator.SetPlanned"/>). Returns the Task with its Tags and Subtasks loaded.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Task has this id.</exception>
    Task<TaskItem> SetReminderAsync(Guid taskId, DateTime? reminderUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Snoozes the Reminder of a Task until now + <paramref name="duration"/> and re-arms it; the planned
    /// Reminder is left unchanged (see <see cref="ReminderCalculator.Snooze"/>). A completed Task is left as it
    /// is. Returns the Task with its Tags and Subtasks loaded.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Task has this id.</exception>
    Task<TaskItem> SnoozeAsync(Guid taskId, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the Snooze of a Task and re-arms its planned Reminder (see
    /// <see cref="ReminderCalculator.ClearSnooze"/>). Returns the Task with its Tags and Subtasks loaded.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Task has this id.</exception>
    Task<TaskItem> ClearSnoozeAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>The Reminders due now (see <see cref="ReminderCalculator.IsDue"/>), the one that fires first first.</summary>
    Task<IReadOnlyList<DueReminder>> ListDueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a due Reminder as fired now, so it fires once. Returns false, changing nothing, when it has
    /// fired already, is not due yet, or no longer fires at <paramref name="dueAtUtc"/> because it was edited or
    /// snoozed since it was listed.
    /// </summary>
    Task<bool> TryMarkFiredAsync(Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims a due Reminder like <see cref="TryMarkFiredAsync"/> and returns the fired marker it wrote, which
    /// identifies this claim for <see cref="ReleaseClaimAsync"/>; null when it was not claimed.
    /// </summary>
    Task<DateTime?> TryClaimAsync(Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes a claim whose notification could not be shown, so a later check retries it: clears the fired
    /// marker only while it is still <paramref name="firedUtc"/>. Returns false, changing nothing, when the
    /// Reminder was re-armed or claimed again since.
    /// </summary>
    Task<bool> ReleaseClaimAsync(Guid taskId, DateTime firedUtc, CancellationToken cancellationToken = default);
}

/// <summary>Tells listeners that Tasks were saved (added, edited, completed, deleted or restored).</summary>
public interface ITaskChangeNotifier
{
    /// <summary>Raised after a save that changed Tasks; may be raised on any thread.</summary>
    event Action? TasksChanged;
}
