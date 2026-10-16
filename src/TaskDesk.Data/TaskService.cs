using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

internal sealed class TaskService(
    IDbContextFactory<TaskDeskDbContext> contextFactory,
    TimeProvider clock,
    ILogger<TaskService> logger) : ITaskService
{
    public async Task<TaskItem> CreateTaskAsync(string title, CancellationToken cancellationToken = default)
    {
        var normalizedTitle = TaskTitle.Normalize(title);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var listId = await context.Lists
            .Where(l => l.IsDefault)
            .Select(l => l.Id)
            .SingleAsync(cancellationToken);

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

    public async Task<IReadOnlyList<TaskItem>> ListDefaultListTasksAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Tasks
            .AsNoTracking()
            .Where(t => context.Lists.Any(l => l.IsDefault && l.Id == t.ListId))
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.CreatedUtc)
            .ToListAsync(cancellationToken);
    }
}
