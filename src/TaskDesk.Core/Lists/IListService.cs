using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Lists;

/// <summary>A List with the number of its open (not completed) Tasks.</summary>
public sealed record ListSummary(TaskList List, int OpenTaskCount);

/// <summary>Another List already has this name (compared ignoring case).</summary>
public sealed class DuplicateListNameException(string name)
    : InvalidOperationException($"A List named '{name}' already exists.");

/// <summary>The Default List cannot be deleted.</summary>
public sealed class DefaultListDeletionException()
    : InvalidOperationException("The Default List cannot be deleted.");

/// <summary>Use cases for managing Lists.</summary>
public interface IListService
{
    /// <summary>Lists every List in the owner's order with its open-Task count.</summary>
    Task<IReadOnlyList<ListSummary>> GetListsAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a List at the end of the order.</summary>
    /// <exception cref="ArgumentException">The name is empty or too long.</exception>
    /// <exception cref="DuplicateListNameException"/>
    Task<TaskList> CreateListAsync(string name, CancellationToken cancellationToken = default);

    /// <exception cref="ArgumentException">The name is empty or too long.</exception>
    /// <exception cref="DuplicateListNameException"/>
    /// <exception cref="KeyNotFoundException">No such List.</exception>
    Task RenameListAsync(Guid listId, string name, CancellationToken cancellationToken = default);

    /// <param name="listId">The List to colour.</param>
    /// <param name="colorHex">#RRGGBB, or null to clear.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <exception cref="ArgumentException">Not #RRGGBB.</exception>
    /// <exception cref="KeyNotFoundException">No such List.</exception>
    Task SetListColorAsync(Guid listId, string? colorHex, CancellationToken cancellationToken = default);

    /// <summary>Sets the order of all Lists; must name every existing List exactly once.</summary>
    /// <exception cref="ArgumentException">The ids are not exactly the existing Lists.</exception>
    Task ReorderListsAsync(IReadOnlyList<Guid> orderedListIds, CancellationToken cancellationToken = default);

    /// <summary>Deletes a List and, with it, its Tasks (and their Subtasks and Tag links).</summary>
    /// <exception cref="DefaultListDeletionException"/>
    /// <exception cref="KeyNotFoundException">No such List.</exception>
    Task DeleteListAsync(Guid listId, CancellationToken cancellationToken = default);
}
