using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Export;

/// <summary>Everything an export writes. Tasks carry their Tags and Subtasks.</summary>
/// <param name="Lists">Every List.</param>
/// <param name="Tags">Every Tag, including unused ones.</param>
/// <param name="Tasks">Every Task with <see cref="TaskItem.Tags"/> and <see cref="TaskItem.Subtasks"/> loaded.</param>
public sealed record ExportData(
    IReadOnlyList<TaskList> Lists,
    IReadOnlyList<Tag> Tags,
    IReadOnlyList<TaskItem> Tasks);
