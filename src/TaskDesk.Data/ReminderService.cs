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
        ChangeAsync(taskId, t => ReminderCalculator.SetPlanned(t, reminderUtc, clock.GetUtcNow()), "Reminder changed", cancellationToken);

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
            .WithDetails()
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
            .Where(t => !t.IsCompleted && (t.SnoozedUntilUtc == null || t.SnoozedUntilUtc <= nowUtc)
                && ((t.ReminderUtc <= nowUtc && (t.NotifiedThroughUtc == null || t.ReminderUtc > t.NotifiedThroughUtc))
                    || (t.SnoozedUntilUtc <= nowUtc
                        && (t.NotifiedThroughUtc == null || t.SnoozedUntilUtc > t.NotifiedThroughUtc))))
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
            ReminderCalculator.DueInstant(t, now)!.Value))];
    }

    public async Task<bool> TryMarkFiredAsync(
        Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default) =>
        await TryClaimAsync(taskId, dueAtUtc, cancellationToken) is not null;

    public async Task<DateTime?> TryClaimAsync(
        Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default)
    {
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        DateTime? claim = DateTime.SpecifyKind(dueAtUtc, DateTimeKind.Utc);
        if (claim > nowUtc)
        {
            return null; // not due yet
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        // One conditional UPDATE that advances "notified through" to the listed due instant, so two checks never
        // both claim the same moment. A Task completed, snoozed, edited (the past-moment rule moves "notified
        // through" to now) or claimed since it was listed no longer matches.
        var updated = await context.Tasks
            .Where(t => t.Id == taskId && !t.IsCompleted
                && (t.SnoozedUntilUtc == null || t.SnoozedUntilUtc <= nowUtc)
                && (t.NotifiedThroughUtc == null || t.NotifiedThroughUtc < claim))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.NotifiedThroughUtc, claim), cancellationToken);
        if (updated > 0)
        {
            logger.LogInformation("Task {TaskId} Reminder fired", taskId);
        }

        return updated > 0 ? claim : null;
    }

    public async Task<bool> ReleaseClaimAsync(
        Guid taskId, DateTime firedUtc, CancellationToken cancellationToken = default)
    {
        DateTime? claim = DateTime.SpecifyKind(firedUtc, DateTimeKind.Utc);
        // Just before the claimed due instant, so that moment is due again; every earlier moment the claim
        // covered collapses into it, as it did when it was listed.
        DateTime? released = claim.Value.AddTicks(-1);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        // Conditional on this claim, so a re-armed or re-claimed Task is left alone.
        var updated = await context.Tasks
            .Where(t => t.Id == taskId && t.NotifiedThroughUtc == claim)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.NotifiedThroughUtc, released), cancellationToken);
        if (updated > 0)
        {
            logger.LogWarning("Task {TaskId} Reminder could not be shown; released for a later check", taskId);
        }

        return updated > 0;
    }
}
