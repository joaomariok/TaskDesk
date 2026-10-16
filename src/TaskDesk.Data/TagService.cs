using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

internal sealed class TagService(
    IDbContextFactory<TaskDeskDbContext> contextFactory,
    ILogger<TagService> logger) : ITagService
{
    public async Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Tags.AsNoTracking().OrderBy(t => t.NormalizedName).ToListAsync(cancellationToken);
    }

    public async Task<Tag> CreateTagAsync(string name, CancellationToken cancellationToken = default)
    {
        TagName.Validate(name);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var normalized = TagName.Normalize(name);
        if (await context.Tags.AnyAsync(t => t.NormalizedName == normalized, cancellationToken))
        {
            throw new InvalidOperationException($"A Tag named '{name}' already exists.");
        }

        var tag = NewTag(name, normalized);
        context.Tags.Add(tag);
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Tag {TagId} created", tag.Id);
        return tag;
    }

    public async Task<Tag> AddTagToTaskAsync(Guid taskId, string name, CancellationToken cancellationToken = default)
    {
        TagName.Validate(name);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var normalized = TagName.Normalize(name);
        var tag = await context.Tags.SingleOrDefaultAsync(t => t.NormalizedName == normalized, cancellationToken);
        if (tag is null)
        {
            tag = NewTag(name, normalized);
            context.Tags.Add(tag);
        }

        var linked = await context.TaskTags.AnyAsync(l => l.TaskId == taskId && l.TagId == tag.Id, cancellationToken);
        if (!linked)
        {
            context.TaskTags.Add(new TaskTag { TaskId = taskId, TagId = tag.Id });
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Tag {TagId} added to Task {TaskId}", tag.Id, taskId);
        return tag;
    }

    public async Task RemoveTagFromTaskAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.TaskTags
            .Where(l => l.TaskId == taskId && l.TagId == tagId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Tags.Where(t => t.Id == tagId).ExecuteDeleteAsync(cancellationToken);
        logger.LogInformation("Tag {TagId} deleted", tagId);
    }

    public async Task<IReadOnlyList<TaskItem>> ListTasksByTagAsync(
        Guid tagId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Tasks
            .AsNoTracking()
            .Include(t => t.Tags.OrderBy(g => g.NormalizedName))
            .Include(t => t.Subtasks.OrderBy(s => s.SortOrder))
            .Where(t => t.Tags.Any(g => g.Id == tagId))
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.CreatedUtc)
            .ToListAsync(cancellationToken);
    }

    private static Tag NewTag(string name, string normalized) =>
        new() { Id = Guid.CreateVersion7(), Name = name, NormalizedName = normalized };
}
