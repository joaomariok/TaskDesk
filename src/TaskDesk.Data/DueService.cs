using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

internal sealed class DueService(
    IDbContextFactory<TaskDeskDbContext> contextFactory,
    TimeProvider clock,
    ILogger<DueService> logger) : IDueService, ITaskSearchService
{
    public async Task<TaskItem> SetDueAsync(
        Guid taskId, DateOnly? dueDate, TimeOnly? dueTime, CancellationToken cancellationToken = default)
    {
        DueCalculator.ValidateDue(dueDate, dueTime);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var task = await context.Tasks
            .Include(t => t.Tags.OrderBy(g => g.NormalizedName))
            .Include(t => t.Subtasks.OrderBy(s => s.SortOrder))
            .SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new KeyNotFoundException($"Task {taskId} does not exist.");

        if (task.DueDate != dueDate || task.DueTime != dueTime)
        {
            task.DueDate = dueDate;
            task.DueTime = dueTime;
            task.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Task {TaskId} Due changed", taskId);
        }

        return task;
    }

    public Task<IReadOnlyList<TaskItem>> ListSmartViewAsync(
        SmartView view, int skip = 0, CancellationToken cancellationToken = default)
        => ListViewAsync(view, skip, null, cancellationToken);

    public async Task<IReadOnlyList<TaskItem>> SearchAsync(
        TaskSearchQuery query, CancellationToken cancellationToken = default)
    {
        var filter = TaskSearchFilter.Create(query);
        if (query.View is { } view)
        {
            return await ListViewAsync(view, query.Skip, filter, cancellationToken);
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var tasks = filter(context.Tasks
            .AsNoTracking()
            .Include(t => t.Tags.OrderBy(g => g.NormalizedName))
            .Include(t => t.Subtasks.OrderBy(s => s.SortOrder)));
        tasks = query.ViewTagId is { } tagId
            ? tasks.Where(t => t.Tags.Any(g => g.Id == tagId))
            : tasks.Where(t => context.Lists.Any(l => l.IsDefault && l.Id == t.ListId));
        return await tasks.OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedUtc).ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<TaskItem>> ListViewAsync(
        SmartView view, int skip, Func<IQueryable<TaskItem>, IQueryable<TaskItem>>? filter,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var zone = clock.LocalTimeZone;
        var today = DueCalculator.LocalToday(now, zone);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<TaskItem> tasks = context.Tasks
            .AsNoTracking()
            .Include(t => t.Tags.OrderBy(g => g.NormalizedName))
            .Include(t => t.Subtasks.OrderBy(s => s.SortOrder));
        if (filter is not null)
        {
            tasks = filter(tasks);
        }

        switch (view)
        {
            case SmartView.Today:
            case SmartView.Overdue:
                // The SQL narrows by date; the Overdue rule itself is the Core predicate, in memory.
                var dated = await tasks
                    .Where(t => !t.IsCompleted && t.DueDate != null && t.DueDate <= today)
                    .ToListAsync(cancellationToken);
                return view == SmartView.Today
                    ? [.. dated.OrderBy(t => t.DueDate).ThenBy(DueTimeOrStartOfDay)
                        .ThenByDescending(t => t.Priority).ThenBy(t => t.SortOrder)]
                    : [.. dated
                        .Where(t => DueCalculator.IsOverdue(t.DueDate, t.DueTime, t.IsCompleted, now, zone))
                        .OrderBy(t => t.DueDate).ThenBy(DueTimeOrStartOfDay).ThenBy(t => t.SortOrder)];

            case SmartView.Upcoming:
                var last = today.AddDays(DueCalculator.UpcomingDays);
                var upcoming = await tasks
                    .Where(t => !t.IsCompleted && t.DueDate != null && t.DueDate > today && t.DueDate <= last)
                    .ToListAsync(cancellationToken);
                return [.. upcoming.OrderBy(t => t.DueDate).ThenBy(DueTimeOrStartOfDay).ThenBy(t => t.SortOrder)];

            case SmartView.All:
                return await tasks
                    .Where(t => !t.IsCompleted)
                    .OrderBy(t => context.Lists.Where(l => l.Id == t.ListId).Select(l => l.SortOrder).First())
                    .ThenBy(t => t.SortOrder)
                    .ThenBy(t => t.CreatedUtc)
                    .ToListAsync(cancellationToken);

            case SmartView.Completed:
                return await tasks
                    .Where(t => t.IsCompleted)
                    .OrderByDescending(t => t.CompletedUtc)
                    .ThenBy(t => t.Id)
                    .Skip(Math.Max(skip, 0))
                    .Take(IDueService.CompletedPageSize)
                    .ToListAsync(cancellationToken);

            default:
                throw new ArgumentOutOfRangeException(nameof(view));
        }
    }

    // A date-only Task sorts before the timed Tasks of the same day.
    private static TimeOnly DueTimeOrStartOfDay(TaskItem task) => task.DueTime ?? TimeOnly.MinValue;
}
