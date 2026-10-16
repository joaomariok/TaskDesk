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
        CreateAsync(title, null, cancellationToken);

    public Task<TaskItem> CreateTaskInListAsync(
        string title, Guid listId, CancellationToken cancellationToken = default) =>
        CreateAsync(title, listId, cancellationToken);

    private async Task<TaskItem> CreateAsync(string title, Guid? requestedListId, CancellationToken cancellationToken)
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
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Task {TaskId} completion set to {IsCompleted}", taskId, isCompleted);
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

        // Removing the Task also removes its Tag links (cascade); the snapshot keeps their ids for Undo.
        context.Tasks.Remove(task);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Task {TaskId} deleted", taskId);
        return new DeletedTask(task, tagIds);
    }

    public async Task<TaskItem> RestoreTaskAsync(DeletedTask deleted, CancellationToken cancellationToken = default)
    {
        var snapshot = deleted.Task;
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
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
            SortOrder = snapshot.SortOrder,
            CreatedUtc = snapshot.CreatedUtc,
            UpdatedUtc = snapshot.UpdatedUtc,
            Tags = tags,
        };
        context.Tasks.Add(restored);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Task {TaskId} restored with {TagCount} Tags", restored.Id, tags.Count);
        return restored;
    }

    private static async Task<TaskItem> FindAsync(
        TaskDeskDbContext context, Guid taskId, CancellationToken cancellationToken)
        => await context.Tasks
            .Include(t => t.Tags.OrderBy(g => g.NormalizedName))
            .SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new KeyNotFoundException($"Task {taskId} does not exist.");

    public async Task<IReadOnlyList<TaskItem>> ListDefaultListTasksAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Tasks
            .AsNoTracking()
            .Include(t => t.Tags.OrderBy(g => g.NormalizedName))
            .Where(t => context.Lists.Any(l => l.IsDefault && l.Id == t.ListId))
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.CreatedUtc)
            .ToListAsync(cancellationToken);
    }
}
