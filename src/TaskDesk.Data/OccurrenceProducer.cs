using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

/// <summary>
/// Produces the next Occurrence of a recurring Task (SPEC 5.1 steps 2 to 4), for a completion or a Skip.
/// Adds it to the context without saving, so the caller saves it in the same transaction as the change
/// that triggered it.
/// </summary>
internal static class OccurrenceProducer
{
    /// <summary>
    /// Adds the next Occurrence of <paramref name="current"/> (loaded with its details) and links it through
    /// NextOccurrenceId. Each of its fixed Reminders keeps the offset from the Due that the Reminder of
    /// <paramref name="current"/> had, measured in <paramref name="zone"/>; its before-Due Reminders are copied as
    /// they are. Returns null, adding nothing, when the Task has no Recurrence or Due date,
    /// has already produced its next Occurrence, or its Series has ended.
    /// </summary>
    public static async Task<TaskItem?> AddNextAsync(
        TaskDeskDbContext context, TaskItem current, DateOnly completionDate, DateTime nowUtc, TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        if (current.Recurrence is not { } recurrence || current.DueDate is not { } currentDue
            || current.NextOccurrenceId is not null)
        {
            return null; // at most once per Occurrence
        }

        if (RecurrenceCalculator.GetNextDueDate(recurrence, currentDue, completionDate) is not { } nextDue)
        {
            return null; // the Series has ended
        }

        current.SeriesId ??= Guid.CreateVersion7();
        var next = new TaskItem
        {
            Id = Guid.CreateVersion7(),
            ListId = current.ListId,
            Title = current.Title,
            Notes = current.Notes,
            Priority = current.Priority,
            DueDate = nextDue,
            DueTime = current.DueTime,
            NotifyAtDue = current.NotifyAtDue,
            Recurrence = recurrence with { }, // an owned value is never shared between two Tasks
            // The Snooze is never carried: it starts cleared and moments already past count as notified
            // (SPEC 5.1 step 3).
            NotifiedThroughUtc = nowUtc,
            SeriesId = current.SeriesId,
            SortOrder = await SortOrderAfterAsync(context, current, cancellationToken),
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            Tags = [.. current.Tags],
        };
        next.Subtasks = [.. current.Subtasks.Select(s => new Subtask
        {
            Id = Guid.CreateVersion7(),
            TaskId = next.Id,
            Title = s.Title,
            IsCompleted = false,
            SortOrder = s.SortOrder,
        })];

        // Each fixed Reminder keeps its offset from the Due (SPEC 5.1 step 3). Two that land on the same moment
        // become one. Before-Due Reminders already count from the Due, so they are copied as they are.
        next.Reminders =
        [
            .. current.Reminders
                .Where(r => r.Kind == ReminderKind.Fixed && r.AtUtc is not null)
                .Select(r => ReminderCalculator.CarryOver(r.AtUtc!.Value, currentDue, current.DueTime, nextDue, zone))
                .Distinct()
                .Order()
                .Select(at => new Reminder
                {
                    Id = Guid.CreateVersion7(),
                    TaskId = next.Id,
                    Kind = ReminderKind.Fixed,
                    AtUtc = at,
                }),
            .. current.Reminders
                .Where(r => r.Kind == ReminderKind.BeforeDue)
                .Select(r => new Reminder
                {
                    Id = Guid.CreateVersion7(),
                    TaskId = next.Id,
                    Kind = ReminderKind.BeforeDue,
                    Amount = r.Amount,
                    Unit = r.Unit,
                }),
        ];

        context.Tasks.Add(next);
        current.NextOccurrenceId = next.Id;
        current.UpdatedUtc = nowUtc;
        return next;
    }

    // Right after the current Occurrence in its List, or at the end when the gap is too small.
    private static async Task<double> SortOrderAfterAsync(
        TaskDeskDbContext context, TaskItem current, CancellationToken cancellationToken)
    {
        var following = await context.Tasks
            .Where(t => t.ListId == current.ListId && t.SortOrder > current.SortOrder)
            .MinAsync(t => (double?)t.SortOrder, cancellationToken);
        if (following is { } next && next - current.SortOrder < 2 * ManualOrder.MinimumGap)
        {
            var last = await context.Tasks.Where(t => t.ListId == current.ListId)
                .MaxAsync(t => t.SortOrder, cancellationToken);
            return last + 1;
        }

        return ManualOrder.Between(current.SortOrder, following);
    }
}
