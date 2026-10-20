using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskDesk.Core.Backups;
using TaskDesk.Core.Export;
using TaskDesk.Core.Import;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

internal sealed class ImportService(
    IDbContextFactory<TaskDeskDbContext> factory,
    IBackupService backups,
    TimeProvider clock,
    ILogger<ImportService> logger) : IImportService
{
    public async Task<ImportResult> ImportJsonAsync(
        string path, ImportMode mode, CancellationToken cancellationToken = default)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ImportException($"The file could not be read: {ex.Message}", ex);
        }

        // The whole file is validated before anything, including the backup, touches the disk.
        var document = JsonImportFormat.Read(bytes);

        var backup = mode == ImportMode.Replace ? await backups.BackUpNowAsync(cancellationToken) : null;

        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var result = mode == ImportMode.Replace
            ? await ReplaceAsync(context, document, cancellationToken)
            : await MergeAsync(context, document, cancellationToken);

        // Invariants re-checked inside the transaction (SPEC 9.3); a failure rolls everything back.
        var defaults = await context.Lists.CountAsync(l => l.IsDefault, cancellationToken);
        if (defaults != 1)
        {
            throw new InvalidOperationException($"The import would leave {defaults} Default Lists; nothing was imported.");
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "Import ({Mode}) finished: Lists {ListsAdded} added, {ListsSkipped} skipped; Tags {TagsAdded} added, "
            + "{TagsSkipped} skipped; Tasks {TasksAdded} added, {TasksSkipped} skipped",
            mode, result.Lists.Added, result.Lists.Skipped, result.Tags.Added, result.Tags.Skipped,
            result.Tasks.Added, result.Tasks.Skipped);
        return result with { BackupPath = backup };
    }

    private async Task<ImportResult> ReplaceAsync(
        TaskDeskDbContext context, ExportDocument document, CancellationToken cancellationToken)
    {
        await context.TaskTags.ExecuteDeleteAsync(cancellationToken);
        await context.Subtasks.ExecuteDeleteAsync(cancellationToken);
        await context.Reminders.ExecuteDeleteAsync(cancellationToken);
        await context.Tasks.ExecuteDeleteAsync(cancellationToken);
        await context.Tags.ExecuteDeleteAsync(cancellationToken);
        await context.Lists.ExecuteDeleteAsync(cancellationToken);

        context.Lists.AddRange(document.Lists.Select(JsonImportFormat.ToList));
        var tags = document.Tags.Select(JsonImportFormat.ToTag).ToDictionary(t => t.Id);
        context.Tags.AddRange(tags.Values);
        var firedAt = clock.GetUtcNow().UtcDateTime;
        foreach (var row in document.Tasks)
        {
            var task = NewTask(row, row.ListId, firedAt);
            task.Tags = [.. row.TagIds.Select(id => tags[id])];
            task.Subtasks = [.. row.Subtasks.Select(s => JsonImportFormat.ToSubtask(s, s.Id, task.Id))];
            context.Tasks.Add(task);
        }

        await context.SaveChangesAsync(cancellationToken);
        return new ImportResult(
            ImportMode.Replace,
            new ImportCounts(document.Lists.Count, 0),
            new ImportCounts(document.Tags.Count, 0),
            new ImportCounts(document.Tasks.Count, 0),
            null);
    }

    /// <summary>
    /// SPEC 9.3 Merge: Lists match by id, then by name; Tags by normalized name, then by id; Tasks are added
    /// only when their id is new. Ids are kept, so Series and Occurrence links survive (ADR 0001).
    /// </summary>
    private async Task<ImportResult> MergeAsync(
        TaskDeskDbContext context, ExportDocument document, CancellationToken cancellationToken)
    {
        var listMap = new Dictionary<Guid, Guid>();
        var localLists = await context.Lists.AsNoTracking().ToListAsync(cancellationToken);
        var localListIds = localLists.Select(l => l.Id).ToHashSet();
        var localListNames = localLists.ToDictionary(l => l.Name, l => l.Id, JsonImportFormat.ListNameComparer);
        var nextSortOrder = localLists.Count == 0 ? 0 : localLists.Max(l => l.SortOrder) + 1;
        var listsAdded = 0;
        foreach (var row in document.Lists)
        {
            if (localListIds.Contains(row.Id))
            {
                listMap[row.Id] = row.Id;
            }
            else if (localListNames.TryGetValue(row.Name.Trim(), out var sameName))
            {
                listMap[row.Id] = sameName;
            }
            else
            {
                // An incoming default List that matches nothing is an ordinary List: the local default stays.
                var list = JsonImportFormat.ToList(row);
                list.IsDefault = false;
                list.SortOrder = nextSortOrder++;
                context.Lists.Add(list);
                listMap[row.Id] = list.Id;
                listsAdded++;
            }
        }

        var localTags = await context.Tags.ToListAsync(cancellationToken);
        var tagsByName = localTags.ToDictionary(t => t.NormalizedName, StringComparer.Ordinal);
        var tagsById = localTags.ToDictionary(t => t.Id);
        var tagMap = new Dictionary<Guid, Tag>();
        var tagsAdded = 0;
        foreach (var row in document.Tags)
        {
            if (tagsByName.TryGetValue(TagName.Normalize(row.Name), out var sameName))
            {
                tagMap[row.Id] = sameName;
            }
            else if (tagsById.TryGetValue(row.Id, out var sameId))
            {
                // Same Tag, renamed locally since the export: keep the local name.
                tagMap[row.Id] = sameId;
            }
            else
            {
                var tag = JsonImportFormat.ToTag(row);
                context.Tags.Add(tag);
                tagMap[row.Id] = tag;
                tagsAdded++;
            }
        }

        var fileTaskIds = document.Tasks.Select(t => t.Id).ToList();
        var existingTasks = (await context.Tasks
            .Where(t => fileTaskIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
        var fileSubtaskIds = document.Tasks.SelectMany(t => t.Subtasks).Select(s => s.Id).ToList();
        var existingSubtasks = (await context.Subtasks
            .Where(s => fileSubtaskIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var now = clock.GetUtcNow().UtcDateTime;
        var tasksAdded = 0;
        foreach (var row in document.Tasks.Where(t => !existingTasks.Contains(t.Id)))
        {
            var task = NewTask(row, listMap[row.ListId], now);
            task.Tags = [.. row.TagIds.Select(id => tagMap[id]).DistinctBy(t => t.Id)];

            // A Subtask id already used by a local Task's Subtask gets a fresh id rather than failing the import.
            task.Subtasks =
            [
                .. row.Subtasks.Select(s => JsonImportFormat.ToSubtask(
                    s, existingSubtasks.Contains(s.Id) ? Guid.CreateVersion7() : s.Id, task.Id)),
            ];
            context.Tasks.Add(task);
            tasksAdded++;
        }

        await context.SaveChangesAsync(cancellationToken);
        return new ImportResult(
            ImportMode.Merge,
            new ImportCounts(listsAdded, document.Lists.Count - listsAdded),
            new ImportCounts(tagsAdded, document.Tags.Count - tagsAdded),
            new ImportCounts(tasksAdded, document.Tasks.Count - tasksAdded),
            null);
    }

    /// <summary>
    /// The Task a row becomes. The export does not carry "notified through" (SPEC 9.1), so moments already
    /// past count as notified (the past-moment rule): importing an old file must not flood notifications.
    /// </summary>
    private static TaskItem NewTask(ExportTask row, Guid listId, DateTime now)
    {
        var task = JsonImportFormat.ToTask(row, listId);
        task.NotifiedThroughUtc = now;
        return task;
    }
}
