using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public class TaskDetailsServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(TempDatabase Db, ITaskService Service)> StartAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return (db, db.Get<ITaskService>());
    }

    [Fact]
    public async Task Completing_sets_the_state_and_time_and_un_completing_clears_both()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var task = await service.CreateTaskAsync("pay bills", Ct);
            db.Clock.Advance(TimeSpan.FromHours(1));
            var expected = db.Clock.GetUtcNow().UtcDateTime;

            await service.SetCompletedAsync(task.Id, true, Ct);

            var completed = Assert.Single(await service.ListDefaultListTasksAsync(Ct));
            Assert.True(completed.IsCompleted);
            Assert.Equal(expected, completed.CompletedUtc);
            Assert.Equal(DateTimeKind.Utc, completed.CompletedUtc!.Value.Kind);

            await service.SetCompletedAsync(task.Id, false, Ct);

            var open = Assert.Single(await service.ListDefaultListTasksAsync(Ct));
            Assert.False(open.IsCompleted);
            Assert.Null(open.CompletedUtc);
        }
    }

    [Fact]
    public async Task Completing_an_already_completed_Task_keeps_the_original_completion_time()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var task = await service.CreateTaskAsync("a", Ct);
            await service.SetCompletedAsync(task.Id, true, Ct);
            var first = (await service.ListDefaultListTasksAsync(Ct)).Single().CompletedUtc;
            db.Clock.Advance(TimeSpan.FromHours(2));

            await service.SetCompletedAsync(task.Id, true, Ct);

            Assert.Equal(first, (await service.ListDefaultListTasksAsync(Ct)).Single().CompletedUtc);
        }
    }

    [Fact]
    public async Task Completed_Tasks_keep_their_place_in_the_manual_order()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            await service.CreateTaskAsync("a", Ct);
            var b = await service.CreateTaskAsync("b", Ct);
            await service.CreateTaskAsync("c", Ct);

            await service.SetCompletedAsync(b.Id, true, Ct);
            await service.SetCompletedAsync(b.Id, false, Ct);

            Assert.Equal(["a", "b", "c"], (await service.ListDefaultListTasksAsync(Ct)).Select(t => t.Title));
        }
    }

    [Fact]
    public async Task Editing_stores_the_title_notes_and_Priority_and_persists_across_a_restart()
    {
        using var first = new TempDatabase();
        await first.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        var service = first.Get<ITaskService>();
        var task = await service.CreateTaskAsync("old", Ct);
        first.Clock.Advance(TimeSpan.FromMinutes(5));

        await service.UpdateTaskAsync(task.Id, "  new title ", "line 1\nline 2", Priority.High, Ct);

        using var second = new TempDatabase(first.Paths);
        await second.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        var stored = Assert.Single(await second.Get<ITaskService>().ListDefaultListTasksAsync(Ct));
        Assert.Equal("new title", stored.Title);
        Assert.Equal("line 1\nline 2", stored.Notes);
        Assert.Equal(Priority.High, stored.Priority);
        Assert.Equal(first.Clock.GetUtcNow().UtcDateTime, stored.UpdatedUtc);
    }

    [Fact]
    public async Task Blank_notes_are_stored_as_none()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var task = await service.CreateTaskAsync("a", Ct);
            await service.UpdateTaskAsync(task.Id, "a", "text", Priority.None, Ct);

            await service.UpdateTaskAsync(task.Id, "a", "  ", Priority.None, Ct);

            Assert.Null((await service.ListDefaultListTasksAsync(Ct)).Single().Notes);
        }
    }

    [Fact]
    public async Task Notes_of_exactly_10000_characters_are_accepted_and_longer_ones_rejected()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var task = await service.CreateTaskAsync("a", Ct);

            await service.UpdateTaskAsync(task.Id, "a", new string('x', 10_000), Priority.None, Ct);
            await Assert.ThrowsAsync<ArgumentException>(
                () => service.UpdateTaskAsync(task.Id, "a", new string('x', 10_001), Priority.None, Ct));

            Assert.Equal(10_000, (await service.ListDefaultListTasksAsync(Ct)).Single().Notes!.Length);
        }
    }

    [Fact]
    public async Task Editing_with_an_empty_title_or_unknown_id_is_rejected()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var task = await service.CreateTaskAsync("keep", Ct);

            await Assert.ThrowsAsync<ArgumentException>(
                () => service.UpdateTaskAsync(task.Id, "  ", null, Priority.None, Ct));
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => service.UpdateTaskAsync(Guid.NewGuid(), "x", null, Priority.None, Ct));

            Assert.Equal("keep", (await service.ListDefaultListTasksAsync(Ct)).Single().Title);
        }
    }

    [Fact]
    public async Task Delete_removes_the_Task_immediately()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var a = await service.CreateTaskAsync("a", Ct);
            await service.CreateTaskAsync("b", Ct);

            var snapshot = await service.DeleteTaskAsync(a.Id, Ct);

            Assert.Equal(a.Id, snapshot.Task.Id);
            Assert.Equal(["b"], (await service.ListDefaultListTasksAsync(Ct)).Select(t => t.Title));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteTaskAsync(a.Id, Ct));
        }
    }

    [Fact]
    public async Task Undo_re_inserts_the_deleted_Task_with_the_same_id_order_and_details()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            await service.CreateTaskAsync("a", Ct);
            var b = await service.CreateTaskAsync("b", Ct);
            await service.CreateTaskAsync("c", Ct);
            await service.UpdateTaskAsync(b.Id, "b", "notes", Priority.Medium, Ct);
            await service.SetCompletedAsync(b.Id, true, Ct);
            await db.Get<IDueService>().SetDueAsync(b.Id, new DateOnly(2026, 10, 24), new TimeOnly(8, 15), Ct);
            var before = (await service.ListDefaultListTasksAsync(Ct)).Single(t => t.Id == b.Id);

            var snapshot = await service.DeleteTaskAsync(b.Id, Ct);
            db.Clock.Advance(TimeSpan.FromSeconds(8));
            await service.RestoreTaskAsync(snapshot, Ct);

            var tasks = await service.ListDefaultListTasksAsync(Ct);
            Assert.Equal(["a", "b", "c"], tasks.Select(t => t.Title));
            var after = tasks[1];
            Assert.Equal(before.Id, after.Id);
            Assert.Equal(before.ListId, after.ListId);
            Assert.Equal(before.Notes, after.Notes);
            Assert.Equal(before.Priority, after.Priority);
            Assert.Equal(before.IsCompleted, after.IsCompleted);
            Assert.Equal(before.CompletedUtc, after.CompletedUtc);
            Assert.Equal(new DateOnly(2026, 10, 24), after.DueDate);
            Assert.Equal(new TimeOnly(8, 15), after.DueTime);
            Assert.Equal(before.SortOrder, after.SortOrder);
            Assert.Equal(before.CreatedUtc, after.CreatedUtc);
            Assert.Equal(before.UpdatedUtc, after.UpdatedUtc);
        }
    }

    [Fact]
    public async Task Undo_restores_the_Tags_of_the_deleted_Task()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var tags = db.Get<ITagService>();
            var task = await service.CreateTaskAsync("tagged", Ct);
            await tags.AddTagToTaskAsync(task.Id, "work", Ct);
            await tags.AddTagToTaskAsync(task.Id, "home", Ct);

            var snapshot = await service.DeleteTaskAsync(task.Id, Ct);
            Assert.Equal(2, snapshot.TagIds.Count);
            Assert.Empty(await tags.ListTasksByTagAsync(snapshot.TagIds[0], Ct));

            var restored = await service.RestoreTaskAsync(snapshot, Ct);

            Assert.Equal(["home", "work"], restored.Tags.Select(t => t.Name));
            var stored = Assert.Single(await service.ListDefaultListTasksAsync(Ct));
            Assert.Equal(["home", "work"], stored.Tags.Select(t => t.Name));
            var work = (await tags.ListTagsAsync(Ct)).Single(t => t.Name == "work");
            Assert.Equal([task.Id], (await tags.ListTasksByTagAsync(work.Id, Ct)).Select(t => t.Id));
        }
    }

    [Fact]
    public async Task Undo_skips_a_Tag_that_was_deleted_in_the_meantime()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var tags = db.Get<ITagService>();
            var task = await service.CreateTaskAsync("tagged", Ct);
            var gone = await tags.AddTagToTaskAsync(task.Id, "gone", Ct);
            await tags.AddTagToTaskAsync(task.Id, "kept", Ct);
            var snapshot = await service.DeleteTaskAsync(task.Id, Ct);
            await tags.DeleteTagAsync(gone.Id, Ct);

            await service.RestoreTaskAsync(snapshot, Ct);

            var stored = Assert.Single(await service.ListDefaultListTasksAsync(Ct));
            Assert.Equal(["kept"], stored.Tags.Select(t => t.Name));
            Assert.Equal(["kept"], (await tags.ListTagsAsync(Ct)).Select(t => t.Name));
        }
    }

    [Fact]
    public async Task Completing_and_editing_return_the_Task_with_its_Tags()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var task = await service.CreateTaskAsync("tagged", Ct);
            await db.Get<ITagService>().AddTagToTaskAsync(task.Id, "work", Ct);

            var completed = await service.SetCompletedAsync(task.Id, true, Ct);
            var edited = await service.UpdateTaskAsync(task.Id, "renamed", null, Priority.Low, Ct);

            Assert.Equal(["work"], completed.Tags.Select(t => t.Name));
            Assert.Equal(["work"], edited.Tags.Select(t => t.Name));
        }
    }

    [Fact]
    public async Task Restoring_a_Task_that_exists_already_is_rejected()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var a = await service.CreateTaskAsync("a", Ct);
            var snapshot = await service.DeleteTaskAsync(a.Id, Ct);
            await service.RestoreTaskAsync(snapshot, Ct);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreTaskAsync(snapshot, Ct));

            Assert.Single(await service.ListDefaultListTasksAsync(Ct));
        }
    }
}
