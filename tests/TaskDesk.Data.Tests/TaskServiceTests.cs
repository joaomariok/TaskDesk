using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public class TaskServiceTests
{
    private static async Task<(TempDatabase Db, ITaskService Service)> StartAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(TestContext.Current.CancellationToken);
        return (db, db.Get<ITaskService>());
    }

    [Fact]
    public async Task Created_Task_belongs_to_the_Default_List_and_has_a_trimmed_title()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var created = await service.CreateTaskAsync("  Buy milk ", TestContext.Current.CancellationToken);

            var defaultList = Assert.Single(await db.QueryAsync(c => c.Lists));
            Assert.Equal("Buy milk", created.Title);
            Assert.Equal(defaultList.Id, created.ListId);
            Assert.False(created.IsCompleted);
        }
    }

    [Fact]
    public async Task Created_Tasks_are_listed_in_creation_order()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            await service.CreateTaskAsync("first", TestContext.Current.CancellationToken);
            await service.CreateTaskAsync("second", TestContext.Current.CancellationToken);
            await service.CreateTaskAsync("third", TestContext.Current.CancellationToken);

            var titles = (await service.ListDefaultListTasksAsync(TestContext.Current.CancellationToken)).Select(t => t.Title);

            Assert.Equal(["first", "second", "third"], titles);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_title_is_rejected_and_nothing_is_stored(string title)
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateTaskAsync(title, TestContext.Current.CancellationToken));

            Assert.Empty(await service.ListDefaultListTasksAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Tasks_persist_across_a_restart()
    {
        using var first = new TempDatabase();
        await first.Get<IDatabaseInitializer>().InitializeAsync(TestContext.Current.CancellationToken);
        await first.Get<ITaskService>().CreateTaskAsync("survives restart", TestContext.Current.CancellationToken);

        // A second container over the same folder stands in for restarting the app.
        using var second = new TempDatabase(first.Paths);
        await second.Get<IDatabaseInitializer>().InitializeAsync(TestContext.Current.CancellationToken);

        var tasks = await second.Get<ITaskService>().ListDefaultListTasksAsync(TestContext.Current.CancellationToken);
        Assert.Equal("survives restart", Assert.Single(tasks).Title);
        Assert.Single(await second.QueryAsync(c => c.Lists));
    }

    [Fact]
    public async Task Instants_are_stored_as_UTC_and_read_back_with_Kind_Utc()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var created = await service.CreateTaskAsync("utc check", TestContext.Current.CancellationToken);

            var stored = Assert.Single(await service.ListDefaultListTasksAsync(TestContext.Current.CancellationToken));
            var expected = new DateTime(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc);
            Assert.Equal(DateTimeKind.Utc, stored.CreatedUtc.Kind);
            Assert.Equal(DateTimeKind.Utc, stored.UpdatedUtc.Kind);
            Assert.Equal(expected, stored.CreatedUtc);
            Assert.Equal(expected, created.CreatedUtc);

            var list = Assert.Single(await db.QueryAsync(c => c.Lists));
            Assert.Equal(DateTimeKind.Utc, list.CreatedUtc.Kind);
        }
    }
}
