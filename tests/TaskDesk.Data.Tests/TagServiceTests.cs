using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public class TagServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(TempDatabase Db, ITagService Tags, ITaskService Tasks)> StartAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return (db, db.Get<ITagService>(), db.Get<ITaskService>());
    }

    [Fact]
    public async Task Tag_names_are_unique_regardless_of_case()
    {
        var (db, tags, _) = await StartAsync();
        using (db)
        {
            await tags.CreateTagAsync("Work", Ct);

            await Assert.ThrowsAsync<InvalidOperationException>(() => tags.CreateTagAsync("wORK", Ct));

            Assert.Equal("Work", Assert.Single(await tags.ListTagsAsync(Ct)).Name);
        }
    }

    [Fact]
    public async Task The_database_enforces_case_insensitive_uniqueness()
    {
        var (db, _, _) = await StartAsync();
        using (db)
        {
            await using var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(Ct);
            context.Tags.Add(new Tag { Id = Guid.NewGuid(), Name = "a", NormalizedName = "A" });
            context.Tags.Add(new Tag { Id = Guid.NewGuid(), Name = "A", NormalizedName = "A" });

            await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("#lead")]
    public async Task Invalid_names_are_rejected_and_nothing_is_stored(string name)
    {
        var (db, tags, tasks) = await StartAsync();
        using (db)
        {
            var task = await tasks.CreateTaskAsync("t", Ct);

            await Assert.ThrowsAsync<ArgumentException>(() => tags.CreateTagAsync(name, Ct));
            await Assert.ThrowsAsync<ArgumentException>(() => tags.AddTagToTaskAsync(task.Id, name, Ct));

            Assert.Empty(await tags.ListTagsAsync(Ct));
        }
    }

    [Fact]
    public async Task Adding_a_Tag_to_a_Task_reuses_an_existing_Tag_ignoring_case()
    {
        var (db, tags, tasks) = await StartAsync();
        using (db)
        {
            var first = await tasks.CreateTaskAsync("one", Ct);
            var second = await tasks.CreateTaskAsync("two", Ct);

            var a = await tags.AddTagToTaskAsync(first.Id, "Work", Ct);
            var b = await tags.AddTagToTaskAsync(second.Id, "work", Ct);
            await tags.AddTagToTaskAsync(second.Id, "WORK", Ct); // already linked: no-op

            Assert.Equal(a.Id, b.Id);
            Assert.Single(await tags.ListTagsAsync(Ct));
            Assert.Equal(2, (await tags.ListTasksByTagAsync(a.Id, Ct)).Count);
            var listed = await tasks.ListDefaultListTasksAsync(Ct);
            Assert.All(listed, t => Assert.Equal(["Work"], t.Tags.Select(g => g.Name)));
        }
    }

    [Fact]
    public async Task Listing_by_Tag_returns_only_Tasks_carrying_it()
    {
        var (db, tags, tasks) = await StartAsync();
        using (db)
        {
            var tagged = await tasks.CreateTaskAsync("tagged", Ct);
            await tasks.CreateTaskAsync("plain", Ct);
            var tag = await tags.AddTagToTaskAsync(tagged.Id, "work", Ct);

            var result = await tags.ListTasksByTagAsync(tag.Id, Ct);

            Assert.Equal("tagged", Assert.Single(result).Title);
        }
    }

    [Fact]
    public async Task Removing_a_Tag_from_a_Task_keeps_the_Tag()
    {
        var (db, tags, tasks) = await StartAsync();
        using (db)
        {
            var task = await tasks.CreateTaskAsync("one", Ct);
            var tag = await tags.AddTagToTaskAsync(task.Id, "work", Ct);

            await tags.RemoveTagFromTaskAsync(task.Id, tag.Id, Ct);

            Assert.Single(await tags.ListTagsAsync(Ct));
            Assert.Empty(await tags.ListTasksByTagAsync(tag.Id, Ct));
            Assert.Empty(await db.QueryAsync(c => c.TaskTags));
        }
    }

    [Fact]
    public async Task Deleting_a_Tag_removes_its_links_but_keeps_the_Tasks()
    {
        var (db, tags, tasks) = await StartAsync();
        using (db)
        {
            var task = await tasks.CreateTaskAsync("one", Ct);
            var tag = await tags.AddTagToTaskAsync(task.Id, "work", Ct);
            var other = await tags.AddTagToTaskAsync(task.Id, "home", Ct);

            await tags.DeleteTagAsync(tag.Id, Ct);

            Assert.Equal(other.Id, Assert.Single(await tags.ListTagsAsync(Ct)).Id);
            var link = Assert.Single(await db.QueryAsync(c => c.TaskTags));
            Assert.Equal(other.Id, link.TagId);
            Assert.Single(await tasks.ListDefaultListTasksAsync(Ct));
        }
    }

    [Fact]
    public async Task Deleting_a_Task_removes_its_Tag_links_but_keeps_the_Tag()
    {
        var (db, tags, tasks) = await StartAsync();
        using (db)
        {
            var task = await tasks.CreateTaskAsync("one", Ct);
            await tags.AddTagToTaskAsync(task.Id, "work", Ct);

            await using (var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(Ct))
            {
                context.Tasks.Remove(await context.Tasks.SingleAsync(Ct));
                await context.SaveChangesAsync(Ct);
            }

            Assert.Empty(await db.QueryAsync(c => c.TaskTags));
            Assert.Single(await tags.ListTagsAsync(Ct));
        }
    }

    [Fact]
    public async Task A_Tag_without_Tasks_remains()
    {
        var (db, tags, _) = await StartAsync();
        using (db)
        {
            await tags.CreateTagAsync("lonely", Ct);

            Assert.Equal("lonely", Assert.Single(await tags.ListTagsAsync(Ct)).Name);
        }
    }
}
