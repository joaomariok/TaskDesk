using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

internal sealed class ReminderService(
    IDbContextFactory<TaskDeskDbContext> contextFactory,
    TimeProvider clock,
    ILogger<ReminderService> logger) : IReminderService
{
    public Task<TaskItem> SetReminderAsync(
        Guid taskId, DateTime? reminderUtc, CancellationToken cancellationToken = default) =>
        ChangeAsync(taskId, t => ReminderCalculator.SetPlanned(t, reminderUtc), "Reminder changed", cancellationToken);

    public Task<TaskItem> SnoozeAsync(Guid taskId, TimeSpan duration, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            taskId, t => ReminderCalculator.Snooze(t, clock.GetUtcNow(), duration), "Reminder snoozed", cancellationToken);

    public Task<TaskItem> ClearSnoozeAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        ChangeAsync(taskId, ReminderCalculator.ClearSnooze, "Snooze cleared", cancellationToken);

    // Loads the Task, applies a Core rule and saves when the rule changed it.
    private async Task<TaskItem> ChangeAsync(
        Guid taskId, Func<TaskItem, bool> change, string what, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var task = await context.Tasks
            .Include(t => t.Tags.OrderBy(g => g.NormalizedName))
            .Include(t => t.Subtasks.OrderBy(s => s.SortOrder))
            .SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new KeyNotFoundException($"Task {taskId} does not exist.");

        if (change(task))
        {
            task.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Task {TaskId}: {Change}", taskId, what);
        }

        return task;
    }

    public async Task<IReadOnlyList<DueReminder>> ListDueAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var nowUtc = now.UtcDateTime;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        // The SQL narrows to the candidates (SPEC 7.4); the due rule itself is the Core function, in memory.
        var candidates = await context.Tasks
            .AsNoTracking()
            .Where(t => !t.IsCompleted && t.ReminderFiredUtc == null
                && (t.SnoozedUntilUtc ?? t.ReminderUtc) <= nowUtc)
            .ToListAsync(cancellationToken);
        var due = ReminderCalculator.SelectDue(candidates, now);
        if (due.Count == 0)
        {
            return [];
        }

        var listIds = due.Select(t => t.ListId).Distinct().ToList();
        var listNames = await context.Lists
            .Where(l => listIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);
        return [.. due.Select(t => new DueReminder(
            t.Id, t.Title, t.DueDate, t.DueTime, listNames.GetValueOrDefault(t.ListId, ""),
            ReminderCalculator.EffectiveUtc(t)!.Value))];
    }

    public async Task<bool> TryMarkFiredAsync(
        Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default) =>
        await TryClaimAsync(taskId, dueAtUtc, cancellationToken) is not null;

    public async Task<DateTime?> TryClaimAsync(
        Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default)
    {
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        DateTime? fired = nowUtc;
        if (dueAtUtc > nowUtc)
        {
            return null; // not due yet
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        // One conditional UPDATE, so two checks never both claim the same Reminder.
        var updated = await context.Tasks
            .Where(t => t.Id == taskId && !t.IsCompleted && t.ReminderFiredUtc == null
                && (t.SnoozedUntilUtc ?? t.ReminderUtc) == dueAtUtc)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReminderFiredUtc, fired), cancellationToken);
        if (updated > 0)
        {
            logger.LogInformation("Task {TaskId} Reminder fired", taskId);
        }

        return updated > 0 ? fired : null;
    }

    public async Task<bool> ReleaseClaimAsync(
        Guid taskId, DateTime firedUtc, CancellationToken cancellationToken = default)
    {
        DateTime? claim = DateTime.SpecifyKind(firedUtc, DateTimeKind.Utc);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        // Conditional on this claim's marker, so a re-armed or re-claimed Reminder is left alone.
        var updated = await context.Tasks
            .Where(t => t.Id == taskId && t.ReminderFiredUtc == claim)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReminderFiredUtc, (DateTime?)null), cancellationToken);
        if (updated > 0)
        {
            logger.LogWarning("Task {TaskId} Reminder could not be shown; released for a later check", taskId);
        }

        return updated > 0;
    }
}
