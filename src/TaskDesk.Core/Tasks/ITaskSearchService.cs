namespace TaskDesk.Core.Tasks;

/// <summary>
/// What to search for within a view. A Task matches when every given condition holds: the text is found
/// (ignoring case) in its title, notes or a Subtask title; its Priority is at least <see cref="MinPriority"/>;
/// it carries every Tag of <see cref="TagIds"/>; when <see cref="HasDue"/>, it has a Due date; and, when
/// <see cref="HasReminder"/>, it has a Reminder.
/// </summary>
/// <param name="Text">Literal text (% and _ are ordinary characters); null or blank means any.</param>
/// <param name="MinPriority">The lowest Priority accepted, or null for any.</param>
/// <param name="TagIds">Tags that must all be on the Task.</param>
/// <param name="HasDue">Only Tasks with a Due date.</param>
/// <param name="View">The Smart View searched in, or null.</param>
/// <param name="ViewTagId">When <paramref name="View"/> is null: the Tag whose Tasks are searched; null
/// searches <paramref name="ViewListId"/>.</param>
/// <param name="Skip">Page offset, honoured by the Completed Smart View only.</param>
/// <param name="HasReminder">Only Tasks with a Reminder.</param>
/// <param name="ViewListId">When <paramref name="View"/> and <paramref name="ViewTagId"/> are null: the List
/// whose Tasks are searched; null searches the Default List.</param>
public sealed record TaskSearchQuery(
    string? Text = null,
    Priority? MinPriority = null,
    IReadOnlyCollection<Guid>? TagIds = null,
    bool HasDue = false,
    SmartView? View = null,
    Guid? ViewTagId = null,
    int Skip = 0,
    bool HasReminder = false,
    Guid? ViewListId = null)
{
    /// <summary>True when no search text and no filter is set.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Text) && MinPriority is null && (TagIds is null || TagIds.Count == 0) && !HasDue
        && !HasReminder;
}

/// <summary>Searches and filters the Tasks of one view.</summary>
public interface ITaskSearchService
{
    /// <summary>
    /// Lists the Tasks of the view named by the query that match it, with their Tags and Subtasks loaded,
    /// in the order the view uses.
    /// </summary>
    Task<IReadOnlyList<TaskItem>> SearchAsync(TaskSearchQuery query, CancellationToken cancellationToken = default);
}
