using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

internal static class TaskSearchFilter
{
    private const string Escape = "\\";

    /// <summary>Builds the conditions of a query as a transformation of a Task query.</summary>
    public static Func<IQueryable<TaskItem>, IQueryable<TaskItem>> Create(TaskSearchQuery query)
    {
        var text = query.Text?.Trim();
        var pattern = string.IsNullOrEmpty(text) ? null : $"%{EscapeLike(text)}%";
        var minPriority = query.MinPriority;
        var tagIds = query.TagIds?.Distinct().ToList() ?? [];
        var hasDue = query.HasDue;
        var hasReminder = query.HasReminder;

        return tasks =>
        {
            if (pattern is not null)
            {
                tasks = tasks.Where(t =>
                    EF.Functions.Like(t.Title, pattern, Escape)
                    || (t.Notes != null && EF.Functions.Like(t.Notes, pattern, Escape))
                    || t.Subtasks.Any(s => EF.Functions.Like(s.Title, pattern, Escape)));
            }

            if (minPriority is { } min)
            {
                tasks = tasks.Where(t => t.Priority >= min);
            }

            foreach (var tagId in tagIds)
            {
                var id = tagId;
                tasks = tasks.Where(t => t.Tags.Any(g => g.Id == id));
            }

            if (hasReminder)
            {
                tasks = tasks.Where(t => t.ReminderUtc != null);
            }

            return hasDue ? tasks.Where(t => t.DueDate != null) : tasks;
        };
    }

    // % and _ are LIKE wildcards; the backslash is declared as the escape character.
    internal static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
