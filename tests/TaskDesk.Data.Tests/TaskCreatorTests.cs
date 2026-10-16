using TaskDesk.Core.Lists;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public class TaskCreatorTests
{
    private static async Task<TempDatabase> StartAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(TestContext.Current.CancellationToken);
        return db;
    }

    [Fact]
    public async Task Creates_the_Task_in_the_chosen_List_with_a_trimmed_title()
    {
        using var db = await StartAsync();
        var work = await db.Get<IListService>().CreateListAsync("Work", TestContext.Current.CancellationToken);

        var created = await db.Get<ITaskCreator>()
            .CreateTaskInListAsync("  Call Ana ", work.Id, TestContext.Current.CancellationToken);

        Assert.Equal(work.Id, created.ListId);
        Assert.Equal("Call Ana", created.Title);
    }

    [Fact]
    public async Task Appends_after_the_Tasks_already_in_that_List()
    {
        using var db = await StartAsync();
        var creator = db.Get<ITaskCreator>();
        var work = await db.Get<IListService>().CreateListAsync("Work", TestContext.Current.CancellationToken);

        var first = await creator.CreateTaskInListAsync("one", work.Id, TestContext.Current.CancellationToken);
        var second = await creator.CreateTaskInListAsync("two", work.Id, TestContext.Current.CancellationToken);

        Assert.True(second.SortOrder > first.SortOrder);
    }

    [Fact]
    public async Task Unknown_List_is_rejected()
    {
        using var db = await StartAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => db.Get<ITaskCreator>()
            .CreateTaskInListAsync("x", Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Empty_title_is_rejected()
    {
        using var db = await StartAsync();
        var work = await db.Get<IListService>().CreateListAsync("Work", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(() => db.Get<ITaskCreator>()
            .CreateTaskInListAsync("   ", work.Id, TestContext.Current.CancellationToken));
    }
}
