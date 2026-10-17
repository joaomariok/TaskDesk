using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

internal sealed class TaskService(
    IDbContextFactory<TaskDeskDbContext> contextFactory,
    TimeProvider clock,
    ILogger<TaskService> logger) : ITaskService, ITaskCreator
{
    public Task<TaskItem> CreateTaskAsync(string title, CancellationToken cancellationToken = default) =>
        CreateAsync(title, null, null, cancellationToken);

    public Task<TaskItem> CreateTaskInListAsync(
        string title, Guid listId, CancellationToken cancellationToken = default) =>
        CreateAsync(title, listId, null, cancellationToken);

    public Task<TaskItem> CreateTaskInListAsync(
        string title, Guid listId, DateOnly? dueDate, CancellationToken cancellationToken = default) =>
        CreateAsync(title, listId, dueDate, cancellationToken);

    private async Task<TaskItem> CreateAsync(
        string title, Guid? requestedListId, DateOnly? dueDate, CancellationToken cancellationToken)
    {
        var normalizedTitle = TaskTitle.Normalize(title);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var listId = requestedListId is { } chosen
            ? await context.Lists.Where(l => l.Id == chosen).Select(l => (Guid?)l.Id).SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"List {chosen} does not exist.")
            : await context.Lists.Where(l => l.IsDefault).Select(l => l.Id).SingleAsync(cancellationToken);

        var lastOrder = await context.Tasks
            .Where(t => t.ListId == listId)
            .MaxAsync(t => (double?)t.SortOrder, cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        var task = new TaskItem
        {
            Id = Guid.CreateVersion7(),
            ListId = listId,
            Title = normalizedTitle,
            DueDate = dueDate,
            SortOrder = (lastOrder ?? 0) + 1,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        context.Tasks.Add(task);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Task {TaskId} created", task.Id);
        return task;
    }

    public async Task MoveTaskAsync(Guid taskId, int newIndex, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var listId = await context.Tasks
            .Where(t => t.Id == taskId)
            .Select(t => (Guid?)t.ListId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Task {taskId} does not exist.");

        var tasks = await context.Tasks
            .Where(t => t.ListId == listId)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.CreatedUtc)
            .ToListAsync(cancellationToken);

        var oldIndex = tasks.FindIndex(t => t.Id == taskId);
        var newOrders = ManualOrder.Move(tasks.ConvertAll(t => t.SortOrder), oldIndex, newIndex);

        var moved = tasks[oldIndex];
        tasks.RemoveAt(oldIndex);
        tasks.Insert(newIndex, moved);

        var now = clock.GetUtcNow().UtcDateTime;
        for (var i = 0; i < tasks.Count; i++)
        {
            if (tasks[i].SortOrder != newOrders[i])
            {
                tasks[i].SortOrder = newOrders[i];
                tasks[i].UpdatedUtc = now;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Task {TaskId} moved to index {Index}", taskId, newIndex);
    }

    public async Task<TaskItem> SetCompletedAsync(
        Guid taskId, bool isCompleted, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var task = await FindAsync(context, taskId, cancellationToken);

        if (task.IsCompleted != isCompleted)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            task.IsCompleted = isCompleted;
            task.CompletedUtc = isCompleted ? now : null;
            task.UpdatedUtc = now;
            if (isCompleted)
            {
                task.SnoozedUntilUtc = null; // a Snooze never outlives completion
            }

            // Completing an Occurrence produces the next one (at most once) in the same transaction.
            // Un-completing never touches the next Occurrence.
            var next = isCompleted
                ? await OccurrenceProducer.AddNextAsync(
                    context, task, DueCalculator.LocalToday(clock.GetUtcNow(), clock.LocalTimeZone), now,
                    clock.LocalTimeZone, cancellationToken)
                : null;
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Task {TaskId} completion set to {IsCompleted}", taskId, isCompleted);
            if (next is not null)
            {
                logger.LogInformation("Occurrence {NextId} produced by Task {TaskId}", next.Id, taskId);
            }
        }

        return task;
    }

    public async Task<TaskItem> UpdateTaskAsync(
        Guid taskId, string title, string? notes, Priority priority, CancellationToken cancellationToken = default)
    {
        var normalizedTitle = TaskTitle.Normalize(title);
        var normalizedNotes = TaskNotes.Normalize(notes);
        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var task = await FindAsync(context, taskId, cancellationToken);

        if (task.Title != normalizedTitle || task.Notes != normalizedNotes || task.Priority != priority)
        {
            task.Title = normalizedTitle;
            task.Notes = normalizedNotes;
            task.Priority = priority;
            task.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Task {TaskId} edited", taskId);
        }

        return task;
    }

    public async Task<DeletedTask> DeleteTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var task = await FindAsync(context, taskId, cancellationToken);
        var tagIds = task.Tags.ConvertAll(t => t.Id);
        var subtasks = task.Subtasks.ConvertAll(CopySubtask);

        // Removing the Task also removes its Tag links and Subtasks (cascade); the snapshot keeps them for Undo.
        context.Tasks.Remove(task);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Task {TaskId} deleted with {SubtaskCount} Subtasks", taskId, subtasks.Count);
        return new DeletedTask(task, tagIds, subtasks);
    }

    public async Task<TaskItem> RestoreTaskAsync(DeletedTask deleted, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var restored = await AddRestoredAsync(context, deleted, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Task {TaskId} restored with {TagCount} Tags and {SubtaskCount} Subtasks",
            restored.Id, restored.Tags.Count, restored.Subtasks.Count);
        return restored;
    }

    /// <summary>Adds a deleted Task back to the context with the same ids, without saving.</summary>
    /// <exception cref="InvalidOperationException">A Task with this id exists already.</exception>
    internal static async Task<TaskItem> AddRestoredAsync(
        TaskDeskDbContext context, DeletedTask deleted, CancellationToken cancellationToken)
    {
        var snapshot = deleted.Task;
        if (await context.Tasks.AnyAsync(t => t.Id == snapshot.Id, cancellationToken))
        {
            throw new InvalidOperationException($"Task {snapshot.Id} exists already.");
        }

        // A Tag deleted since the Task was deleted is not re-created.
        var tagIds = deleted.TagIds.ToList();
        var tags = await context.Tags
            .Where(t => tagIds.Contains(t.Id))
            .OrderBy(t => t.NormalizedName)
            .ToListAsync(cancellationToken);

        var restored = new TaskItem
        {
            Id = snapshot.Id,
            ListId = snapshot.ListId,
            Title = snapshot.Title,
            Notes = snapshot.Notes,
            Priority = snapshot.Priority,
            IsCompleted = snapshot.IsCompleted,
            CompletedUtc = snapshot.CompletedUtc,
            DueDate = snapshot.DueDate,
            DueTime = snapshot.DueTime,
            ReminderUtc = snapshot.ReminderUtc,
            SnoozedUntilUtc = snapshot.SnoozedUntilUtc,
            ReminderFiredUtc = snapshot.ReminderFiredUtc,
            Recurrence = snapshot.Recurrence is { } recurrence ? recurrence with { } : null,
            SeriesId = snapshot.SeriesId,
            NextOccurrenceId = snapshot.NextOccurrenceId,
            SortOrder = snapshot.SortOrder,
            CreatedUtc = snapshot.CreatedUtc,
            UpdatedUtc = snapshot.UpdatedUtc,
            Tags = tags,
            Subtasks = [.. deleted.Subtasks.OrderBy(s => s.SortOrder).Select(CopySubtask)],
        };
        context.Tasks.Add(restored);
        return restored;
    }

    // ---- Subtasks ----

    public async Task<Subtask> AddSubtaskAsync(
        Guid taskId, string title, CancellationToken cancellationToken = default)
    {
        var normalizedTitle = TaskTitle.Normalize(title);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.Tasks.AnyAsync(t => t.Id == taskId, cancellationToken))
        {
            throw new KeyNotFoundException($"Task {taskId} does not exist.");
        }

        var lastOrder = await context.Subtasks
            .Where(s => s.TaskId == taskId)
            .MaxAsync(s => (double?)s.SortOrder, cancellationToken);
        var subtask = new Subtask
        {
            Id = Guid.CreateVersion7(),
            TaskId = taskId,
            Title = normalizedTitle,
            SortOrder = (lastOrder ?? 0) + 1,
        };
        context.Subtasks.Add(subtask);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Subtask {SubtaskId} added to Task {TaskId}", subtask.Id, taskId);
        return subtask;
    }

    public async Task<Subtask> RenameSubtaskAsync(
        Guid subtaskId, string title, CancellationToken cancellationToken = default)
    {
        var normalizedTitle = TaskTitle.Normalize(title);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var subtask = await FindSubtaskAsync(context, subtaskId, cancellationToken);
        if (subtask.Title != normalizedTitle)
        {
            subtask.Title = normalizedTitle;
            await context.SaveChangesAsync(cancellationToken);
        }

        return subtask;
    }

    public async Task<Subtask> SetSubtaskCompletedAsync(
        Guid subtaskId, bool isCompleted, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var subtask = await FindSubtaskAsync(context, subtaskId, cancellationToken);
        if (subtask.IsCompleted != isCompleted)
        {
            subtask.IsCompleted = isCompleted;
            await context.SaveChangesAsync(cancellationToken);
        }

        return subtask;
    }

    public async Task<IReadOnlyList<Subtask>> MoveSubtaskAsync(
        Guid subtaskId, int newIndex, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var taskId = (await FindSubtaskAsync(context, subtaskId, cancellationToken)).TaskId;
        var subtasks = await context.Subtasks
            .Where(s => s.TaskId == taskId)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken);

        var oldIndex = subtasks.FindIndex(s => s.Id == subtaskId);
        var newOrders = ManualOrder.Move(subtasks.ConvertAll(s => s.SortOrder), oldIndex, newIndex);
        var moved = subtasks[oldIndex];
        subtasks.RemoveAt(oldIndex);
        subtasks.Insert(newIndex, moved);
        for (var i = 0; i < subtasks.Count; i++)
        {
            subtasks[i].SortOrder = newOrders[i];
        }

        await context.SaveChangesAsync(cancellationToken);
        return subtasks;
    }

    public async Task<Subtask> DeleteSubtaskAsync(Guid subtaskId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var subtask = await FindSubtaskAsync(context, subtaskId, cancellationToken);
        context.Subtasks.Remove(subtask);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Subtask {SubtaskId} deleted", subtaskId);
        return subtask;
    }

    public async Task<Subtask> RestoreSubtaskAsync(Subtask deleted, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.Tasks.AnyAsync(t => t.Id == deleted.TaskId, cancellationToken))
        {
            throw new KeyNotFoundException($"Task {deleted.TaskId} does not exist.");
        }

        if (await context.Subtasks.AnyAsync(s => s.Id == deleted.Id, cancellationToken))
        {
            throw new InvalidOperationException($"Subtask {deleted.Id} exists already.");
        }

        var restored = CopySubtask(deleted);
        context.Subtasks.Add(restored);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Subtask {SubtaskId} restored", restored.Id);
        return restored;
    }

    internal static Subtask CopySubtask(Subtask source) => new()
    {
        Id = source.Id,
        TaskId = source.TaskId,
        Title = source.Title,
        IsCompleted = source.IsCompleted,
        SortOrder = source.SortOrder,
    };

    private static async Task<Subtask> FindSubtaskAsync(
        TaskDeskDbContext context, Guid subtaskId, CancellationToken cancellationToken)
        => await context.Subtasks.SingleOrDefaultAsync(s => s.Id == subtaskId, cancellationToken)
            ?? throw new KeyNotFoundException($"Subtask {subtaskId} does not exist.");

    internal static async Task<TaskItem> FindAsync(
        TaskDeskDbContext context, Guid taskId, CancellationToken cancellationToken)
        => await context.Tasks
            .Include(t => t.Tags.OrderBy(g => g.NormalizedName))
            .Include(t => t.Subtasks.OrderBy(s => s.SortOrder))
            .SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new KeyNotFoundException($"Task {taskId} does not exist.");

    public async Task<IReadOnlyList<TaskItem>> ListDefaultListTasksAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Tasks
            .AsNoTracking()
            .Include(t => t.Tags.OrderBy(g => g.NormalizedName))
            .Include(t => t.Subtasks.OrderBy(s => s.SortOrder))
            .Where(t => context.Lists.Any(l => l.IsDefault && l.Id == t.ListId))
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.CreatedUtc)
            .ToListAsync(cancellationToken);
    }
}
