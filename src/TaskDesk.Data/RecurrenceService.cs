using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

internal sealed class RecurrenceService(
    IDbContextFactory<TaskDeskDbContext> contextFactory,
    TimeProvider clock,
    ILogger<RecurrenceService> logger) : IRecurrenceService
{
    public async Task<TaskItem> SetRecurrenceAsync(
        Guid taskId, Recurrence? recurrence, CancellationToken cancellationToken = default)
    {
        recurrence?.Validate();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var task = await TaskService.FindAsync(context, taskId, cancellationToken);
        if (recurrence is not null && task.DueDate is null)
        {
            throw new ArgumentException("A Recurrence needs a Due date.", nameof(recurrence));
        }

        if (task.Recurrence != recurrence)
        {
            task.Recurrence = recurrence is null ? null : recurrence with { };
            task.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Task {TaskId} Recurrence changed", taskId);
        }

        return task;
    }

    public async Task<TaskItem> UndoCompletionAsync(
        Guid taskId, Guid? producedOccurrenceId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var task = await TaskService.FindAsync(context, taskId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;

        task.IsCompleted = false;
        task.CompletedUtc = null;
        task.UpdatedUtc = now;
        ReminderCalculator.MarkPastNotified(task, clock.GetUtcNow()); // the past-moment rule
        if (producedOccurrenceId is { } producedId && task.NextOccurrenceId == producedId)
        {
            await RemoveProducedAsync(context, producedId, cancellationToken);
            task.NextOccurrenceId = null;
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Completion of Task {TaskId} undone", taskId);
        return task;
    }

    public async Task<SkippedOccurrence> SkipOccurrenceAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var task = await TaskService.FindAsync(context, taskId, cancellationToken);
        if (task.Recurrence is null)
        {
            throw new InvalidOperationException($"Task {taskId} does not repeat.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var skipDate = DueCalculator.LocalToday(clock.GetUtcNow(), clock.LocalTimeZone);
        var next = await OccurrenceProducer.AddNextAsync(
            context, task, skipDate, now, clock.LocalTimeZone, cancellationToken);

        var snapshot = new DeletedTask(
            task, task.Tags.ConvertAll(t => t.Id), task.Subtasks.ConvertAll(TaskService.CopySubtask));
        context.Tasks.Remove(task);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Occurrence {TaskId} skipped; produced {NextId}", taskId, next?.Id);
        return new SkippedOccurrence(snapshot, next?.Id);
    }

    public async Task<TaskItem> UndoSkipAsync(SkippedOccurrence skipped, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var restored = await TaskService.AddRestoredAsync(context, skipped.Deleted, cancellationToken);
        if (skipped.ProducedOccurrenceId is { } producedId)
        {
            await RemoveProducedAsync(context, producedId, cancellationToken);
            restored.NextOccurrenceId = null;
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Skip of Occurrence {TaskId} undone", restored.Id);
        return restored;
    }

    // The produced Occurrence may have been deleted meanwhile; then there is nothing to remove.
    private static async Task RemoveProducedAsync(
        TaskDeskDbContext context, Guid producedId, CancellationToken cancellationToken)
    {
        if (await context.Tasks.SingleOrDefaultAsync(t => t.Id == producedId, cancellationToken) is { } produced)
        {
            context.Tasks.Remove(produced);
        }
    }
}
