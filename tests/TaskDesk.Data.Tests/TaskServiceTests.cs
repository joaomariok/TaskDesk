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

    [Fact]
    public async Task Moved_Task_takes_its_new_position_and_the_order_persists_across_a_restart()
    {
        using var first = new TempDatabase();
        var ct = TestContext.Current.CancellationToken;
        await first.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var service = first.Get<ITaskService>();
        var a = await service.CreateTaskAsync("a", ct);
        await service.CreateTaskAsync("b", ct);
        await service.CreateTaskAsync("c", ct);

        await service.MoveTaskAsync(a.Id, 2, ct);

        using var second = new TempDatabase(first.Paths);
        await second.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var titles = (await second.Get<ITaskService>().ListDefaultListTasksAsync(ct)).Select(t => t.Title);
        Assert.Equal(["b", "c", "a"], titles);
    }

    [Fact]
    public async Task Moving_between_two_Tasks_only_changes_the_moved_Task()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var ct = TestContext.Current.CancellationToken;
            await service.CreateTaskAsync("a", ct);
            await service.CreateTaskAsync("b", ct);
            var c = await service.CreateTaskAsync("c", ct);

            await service.MoveTaskAsync(c.Id, 1, ct);

            var tasks = await service.ListDefaultListTasksAsync(ct);
            Assert.Equal(["a", "c", "b"], tasks.Select(t => t.Title));
            Assert.Equal([1.0, 1.5, 2.0], tasks.Select(t => t.SortOrder));
        }
    }

    [Fact]
    public async Task Repeated_moves_into_the_same_gap_renormalise_and_keep_the_order_correct()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var ct = TestContext.Current.CancellationToken;
            await service.CreateTaskAsync("first", ct);
            await service.CreateTaskAsync("last", ct);
            const int movers = 40; // each halves the gap after "first"; 2^-40 is far below 1e-6

            for (var i = 1; i <= movers; i++)
            {
                var mover = await service.CreateTaskAsync($"m{i}", ct);
                await service.MoveTaskAsync(mover.Id, 1, ct); // right after "first"
            }

            var tasks = await service.ListDefaultListTasksAsync(ct);
            var expected = new[] { "first" }
                .Concat(Enumerable.Range(1, movers).Reverse().Select(i => $"m{i}"))
                .Append("last");
            Assert.Equal(expected, tasks.Select(t => t.Title));
            for (var i = 1; i < tasks.Count; i++)
            {
                Assert.True(tasks[i].SortOrder - tasks[i - 1].SortOrder >= 1e-6);
            }
        }
    }

    [Fact]
    public async Task Moving_an_unknown_Task_or_to_an_invalid_index_is_rejected()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var ct = TestContext.Current.CancellationToken;
            var a = await service.CreateTaskAsync("a", ct);

            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.MoveTaskAsync(Guid.NewGuid(), 0, ct));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.MoveTaskAsync(a.Id, 1, ct));
        }
    }
}
