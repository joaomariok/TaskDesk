using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

/// <summary>The one shared path every query returning Tasks loads them through.</summary>
internal static class TaskQueryExtensions
{
    /// <summary>
    /// Loads a Task's Tags ordered by normalized name and its Subtasks ordered by sort order. Recurrence is an
    /// owned type stored in the Tasks table, so it loads with the Task and needs no Include.
    /// </summary>
    public static IQueryable<TaskItem> WithDetails(this IQueryable<TaskItem> tasks) =>
        tasks
            .Include(t => t.Tags.OrderBy(g => g.NormalizedName))
            .Include(t => t.Subtasks.OrderBy(s => s.SortOrder));
}
