using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tags;

/// <summary>Use cases for Tags and their links to Tasks.</summary>
public interface ITagService
{
    /// <summary>Lists all Tags, including those without Tasks, ordered by name.</summary>
    Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a Tag with no Tasks.</summary>
    /// <exception cref="ArgumentException">The name breaks the Tag name rules.</exception>
    /// <exception cref="InvalidOperationException">A Tag with that name (ignoring case) exists.</exception>
    Task<Tag> CreateTagAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Attaches the Tag to the Task, creating the Tag if no Tag has that name (ignoring case).</summary>
    /// <exception cref="ArgumentException">The name breaks the Tag name rules.</exception>
    Task<Tag> AddTagToTaskAsync(Guid taskId, string name, CancellationToken cancellationToken = default);

    Task RemoveTagFromTaskAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the Tag and its links to Tasks. The Tasks stay.</summary>
    Task DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default);

    /// <summary>Lists the Tasks carrying the Tag, across Lists, with their Tags loaded.</summary>
    Task<IReadOnlyList<TaskItem>> ListTasksByTagAsync(Guid tagId, CancellationToken cancellationToken = default);
}
