using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

internal sealed class ListService(
    IDbContextFactory<TaskDeskDbContext> contextFactory,
    TimeProvider clock,
    ILogger<ListService> logger) : IListService
{
    public async Task<IReadOnlyList<ListSummary>> GetListsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.Lists
            .AsNoTracking()
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.CreatedUtc)
            .Select(l => new
            {
                List = l,
                Open = context.Tasks.Count(t => t.ListId == l.Id && !t.IsCompleted),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new ListSummary(r.List, r.Open)).ToList();
    }

    public async Task<TaskList> CreateListAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = ListName.Normalize(name);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureNameFreeAsync(context, normalized, exceptListId: null, cancellationToken);

        var last = await context.Lists.MaxAsync(l => (int?)l.SortOrder, cancellationToken);
        var list = new TaskList
        {
            Id = Guid.CreateVersion7(),
            Name = normalized,
            SortOrder = (last ?? -1) + 1,
            CreatedUtc = clock.GetUtcNow().UtcDateTime,
        };
        context.Lists.Add(list);
        await SaveAsync(context, normalized, cancellationToken);

        logger.LogInformation("List {ListId} created", list.Id);
        return list;
    }

    public async Task RenameListAsync(Guid listId, string name, CancellationToken cancellationToken = default)
    {
        var normalized = ListName.Normalize(name);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var list = await FindAsync(context, listId, cancellationToken);
        await EnsureNameFreeAsync(context, normalized, listId, cancellationToken);

        list.Name = normalized;
        await SaveAsync(context, normalized, cancellationToken);
        logger.LogInformation("List {ListId} renamed", listId);
    }

    public async Task SetListColorAsync(Guid listId, string? colorHex, CancellationToken cancellationToken = default)
    {
        var normalized = ListColor.Normalize(colorHex);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var list = await FindAsync(context, listId, cancellationToken);
        list.ColorHex = normalized;
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("List {ListId} colour changed", listId);
    }

    public async Task ReorderListsAsync(
        IReadOnlyList<Guid> orderedListIds, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var lists = await context.Lists.ToListAsync(cancellationToken);

        if (orderedListIds.Count != lists.Count
            || orderedListIds.Distinct().Count() != lists.Count
            || !lists.All(l => orderedListIds.Contains(l.Id)))
        {
            throw new ArgumentException("The ids must be exactly the existing Lists, each once.", nameof(orderedListIds));
        }

        for (var i = 0; i < orderedListIds.Count; i++)
        {
            lists.Single(l => l.Id == orderedListIds[i]).SortOrder = i;
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Lists reordered");
    }

    public async Task DeleteListAsync(Guid listId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var list = await FindAsync(context, listId, cancellationToken);
        if (list.IsDefault)
        {
            throw new DefaultListDeletionException();
        }

        // The Tasks (and, through the same cascade, their Subtasks and Tag links) go with the List.
        context.Lists.Remove(list);
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("List {ListId} deleted", listId);
    }

    private static async Task<TaskList> FindAsync(TaskDeskDbContext context, Guid listId, CancellationToken ct)
        => await context.Lists.SingleOrDefaultAsync(l => l.Id == listId, ct)
            ?? throw new KeyNotFoundException($"No List with id {listId}.");

    // Name column uses the NOCASE collation, so this comparison ignores case.
    private static async Task EnsureNameFreeAsync(
        TaskDeskDbContext context, string name, Guid? exceptListId, CancellationToken ct)
    {
        if (await context.Lists.AnyAsync(l => l.Name == name && l.Id != exceptListId, ct))
        {
            throw new DuplicateListNameException(name);
        }
    }

    private static async Task SaveAsync(TaskDeskDbContext context, string name, CancellationToken ct)
    {
        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Only the unique Name index can fail here: another writer took the name after the pre-check.
            throw new DuplicateListNameException(name);
        }
    }
}
