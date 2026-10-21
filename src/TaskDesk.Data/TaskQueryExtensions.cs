using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

/// <summary>The one shared path every query returning Tasks loads them through.</summary>
internal static class TaskQueryExtensions
{
    /// <summary>
    /// Loads a Task's Tags ordered by normalized name, its Subtasks ordered by sort order and its Reminders ordered
    /// by their fixed instant, before-Due ones first (when a before-Due Reminder fires depends on the Due and the
    /// local rules, so only <see cref="TaskDesk.Core.Reminders.ReminderCalculator.Order"/> orders it). Recurrence
    /// is an owned type stored in the Tasks table, so it loads with the Task and needs no Include.
    /// </summary>
    public static IQueryable<TaskItem> WithDetails(this IQueryable<TaskItem> tasks) =>
        tasks
            .Include(t => t.Tags.OrderBy(g => g.NormalizedName))
            .Include(t => t.Subtasks.OrderBy(s => s.SortOrder))
            .Include(t => t.Reminders.OrderBy(r => r.AtUtc).ThenBy(r => r.Id));
}
