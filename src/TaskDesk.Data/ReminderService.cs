using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

internal sealed class ReminderService(
    IDbContextFactory<TaskDeskDbContext> contextFactory,
    TimeProvider clock,
    IShellSettingsStore settings,
    ILogger<ReminderService> logger) : IReminderService
{
    // The local time zone and the default Reminder time, read each time a rule runs, so a change applies at once.
    private MomentRules Rules() => new(clock.LocalTimeZone, settings.Load().DefaultReminderTime);

    public async Task<TaskItem> AddReminderAsync(
        Guid taskId, ReminderValue reminder, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var task = await FindAsync(context, t => t.Id == taskId, $"Task {taskId} does not exist.", cancellationToken);
        var added = ReminderCalculator.AddReminder(task, reminder, clock.GetUtcNow(), Rules());
        context.Reminders.Add(added);
        await SaveAsync(context, task, "Reminder added", cancellationToken);
        return task;
    }

    public async Task<TaskItem> UpdateReminderAsync(
        Guid reminderId, ReminderValue reminder, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var task = await FindOwnerAsync(context, reminderId, cancellationToken);
        if (ReminderCalculator.UpdateReminder(task, reminderId, reminder, clock.GetUtcNow(), Rules()))
        {
            await SaveAsync(context, task, "Reminder changed", cancellationToken);
        }

        return task;
    }

    public async Task<TaskItem> RemoveReminderAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var task = await FindOwnerAsync(context, reminderId, cancellationToken);
        context.Reminders.Remove(ReminderCalculator.RemoveReminder(task, reminderId, clock.GetUtcNow()));
        await SaveAsync(context, task, "Reminder removed", cancellationToken);
        return task;
    }

    public Task<TaskItem> SetNotifyAtDueAsync(
        Guid taskId, bool notifyAtDue, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            taskId, t => ReminderCalculator.SetNotifyAtDue(t, notifyAtDue, clock.GetUtcNow()),
            "Notify at Due changed", cancellationToken);

    public Task<TaskItem> SetNagPeriodAsync(
        Guid taskId, NagPeriod? period, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            taskId, t => ReminderCalculator.SetNagPeriod(t, period, clock.GetUtcNow()), "Nag changed",
            cancellationToken);

    private static Task<TaskItem> FindOwnerAsync(
        TaskDeskDbContext context, Guid reminderId, CancellationToken cancellationToken) =>
        FindAsync(
            context, t => t.Reminders.Any(r => r.Id == reminderId), $"Reminder {reminderId} does not exist.",
            cancellationToken);

    private static async Task<TaskItem> FindAsync(
        TaskDeskDbContext context, Expression<Func<TaskItem, bool>> match, string missing,
        CancellationToken cancellationToken) =>
        await context.Tasks.WithDetails().SingleOrDefaultAsync(match, cancellationToken)
        ?? throw new KeyNotFoundException(missing);

    private async Task SaveAsync(TaskDeskDbContext context, TaskItem task, string what, CancellationToken cancellationToken)
    {
        task.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Task {TaskId}: {Change}", task.Id, what);
    }

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
        var task = await FindAsync(context, t => t.Id == taskId, $"Task {taskId} does not exist.", cancellationToken);
        if (change(task))
        {
            await SaveAsync(context, task, what, cancellationToken);
        }

        return task;
    }

    public async Task<IReadOnlyList<DueReminder>> ListDueAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var nowUtc = now.UtcDateTime;
        var rules = Rules();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        // The SQL narrows to the candidates (SPEC 7.4); the due rule itself is the Core function, in memory. A
        // before-Due Reminder's moment depends on the Due and the local rules, so every one is a candidate. A Due
        // moment, and every Nag moment after it, is never before local midnight of the Due date, so only a Due up
        // to today can be due.
        var today = DueCalculator.LocalToday(now, rules.Zone);
        var candidates = await context.Tasks
            .AsNoTracking()
            .WithDetails()
            .Where(t => !t.IsCompleted && (t.SnoozedUntilUtc == null || t.SnoozedUntilUtc <= nowUtc)
                && (((t.NotifyAtDue || t.NagPeriod != null) && t.DueDate != null && t.DueDate <= today)
                    || t.Reminders.Any(r => r.Kind == ReminderKind.BeforeDue
                        || (r.AtUtc <= nowUtc && (t.NotifiedThroughUtc == null || r.AtUtc > t.NotifiedThroughUtc)))
                    || (t.SnoozedUntilUtc <= nowUtc
                        && (t.NotifiedThroughUtc == null || t.SnoozedUntilUtc > t.NotifiedThroughUtc))))
            .ToListAsync(cancellationToken);
        var due = ReminderCalculator.SelectDue(candidates, now, rules);
        if (due.Count == 0)
        {
            return [];
        }

        var listIds = due.Select(t => t.ListId).Distinct().ToList();
        var listNames = await context.Lists
            .Where(l => listIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);
        return [.. due.Select(t => Entry(t, listNames.GetValueOrDefault(t.ListId, ""), now, rules))];
    }

    // A due Task's entry. "Overdue" is decided at the moment that fired (its due instant, read in local time), not
    // when it is listed: an on-time Due notification and one shown late at wake read plain, and a Reminder, Snooze
    // end or Nag after the Due reads "Overdue · ".
    private static DueReminder Entry(TaskItem task, string listName, DateTimeOffset now, MomentRules rules)
    {
        var dueAt = ReminderCalculator.DueInstant(task, now, rules)!.Value;
        var overdue = DueCalculator.IsOverdue(
            task.DueDate, task.DueTime, task.IsCompleted, new DateTimeOffset(dueAt, TimeSpan.Zero), rules.Zone);
        return new DueReminder(task.Id, task.Title, task.DueDate, task.DueTime, listName, dueAt, overdue);
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
