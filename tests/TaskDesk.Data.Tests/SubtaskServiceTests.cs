using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public class SubtaskServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(TempDatabase Db, ITaskService Service)> StartAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return (db, db.Get<ITaskService>());
    }

    private static async Task<IReadOnlyList<Subtask>> SubtasksOfAsync(ITaskService service, Guid taskId)
        => (await service.ListDefaultListTasksAsync(Ct)).Single(t => t.Id == taskId).Subtasks;

    [Fact]
    public async Task Added_Subtasks_are_trimmed_and_listed_in_order_with_their_Task()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var task = await service.CreateTaskAsync("trip", Ct);
            await service.AddSubtaskAsync(task.Id, "  book flights ", Ct);
            await service.AddSubtaskAsync(task.Id, "pack", Ct);

            Assert.Equal(["book flights", "pack"], (await SubtasksOfAsync(service, task.Id)).Select(s => s.Title));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Adding_a_Subtask_with_an_empty_title_is_rejected(string title)
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var task = await service.CreateTaskAsync("trip", Ct);

            await Assert.ThrowsAsync<ArgumentException>(() => service.AddSubtaskAsync(task.Id, title, Ct));
            Assert.Empty(await SubtasksOfAsync(service, task.Id));
        }
    }

    [Fact]
    public async Task A_Subtask_title_may_have_500_characters_but_not_501()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var task = await service.CreateTaskAsync("trip", Ct);

            await service.AddSubtaskAsync(task.Id, new string('a', 500), Ct);
            await Assert.ThrowsAsync<ArgumentException>(
                () => service.AddSubtaskAsync(task.Id, new string('a', 501), Ct));
        }
    }

    [Fact]
    public async Task Adding_to_an_unknown_Task_fails()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.AddSubtaskAsync(Guid.NewGuid(), "x", Ct));
        }
    }

    [Fact]
    public async Task Renaming_and_ticking_a_Subtask_persist()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var task = await service.CreateTaskAsync("trip", Ct);
            var subtask = await service.AddSubtaskAsync(task.Id, "pack", Ct);

            await service.RenameSubtaskAsync(subtask.Id, "  pack bags ", Ct);
            await service.SetSubtaskCompletedAsync(subtask.Id, true, Ct);

            var stored = Assert.Single(await SubtasksOfAsync(service, task.Id));
            Assert.Equal("pack bags", stored.Title);
            Assert.True(stored.IsCompleted);

            await service.SetSubtaskCompletedAsync(subtask.Id, false, Ct);
            Assert.False(Assert.Single(await SubtasksOfAsync(service, task.Id)).IsCompleted);
            await Assert.ThrowsAsync<ArgumentException>(() => service.RenameSubtaskAsync(subtask.Id, " ", Ct));
        }
    }

    [Fact]
    public async Task Ticking_every_Subtask_does_not_complete_the_Task_and_completing_the_Task_does_not_tick_them()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var task = await service.CreateTaskAsync("trip", Ct);
            var first = await service.AddSubtaskAsync(task.Id, "a", Ct);
            var second = await service.AddSubtaskAsync(task.Id, "b", Ct);

            await service.SetSubtaskCompletedAsync(first.Id, true, Ct);
            await service.SetSubtaskCompletedAsync(second.Id, true, Ct);
            Assert.False((await service.ListDefaultListTasksAsync(Ct)).Single().IsCompleted);

            await service.SetSubtaskCompletedAsync(first.Id, false, Ct);
            await service.SetSubtaskCompletedAsync(second.Id, false, Ct);
            await service.SetCompletedAsync(task.Id, true, Ct);

            var stored = (await service.ListDefaultListTasksAsync(Ct)).Single();
            Assert.True(stored.IsCompleted);
            Assert.All(stored.Subtasks, s => Assert.False(s.IsCompleted));
        }
    }

    [Fact]
    public async Task Moving_a_Subtask_reorders_the_checklist_and_returns_it()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var task = await service.CreateTaskAsync("trip", Ct);
            await service.AddSubtaskAsync(task.Id, "a", Ct);
            await service.AddSubtaskAsync(task.Id, "b", Ct);
            var c = await service.AddSubtaskAsync(task.Id, "c", Ct);

            var returned = await service.MoveSubtaskAsync(c.Id, 0, Ct);

            Assert.Equal(["c", "a", "b"], returned.Select(s => s.Title));
            Assert.Equal(["c", "a", "b"], (await SubtasksOfAsync(service, task.Id)).Select(s => s.Title));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.MoveSubtaskAsync(c.Id, 3, Ct));
        }
    }

    [Fact]
    public async Task Deleting_a_Subtask_removes_it_and_Restore_puts_it_back_in_place()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var task = await service.CreateTaskAsync("trip", Ct);
            await service.AddSubtaskAsync(task.Id, "a", Ct);
            var b = await service.AddSubtaskAsync(task.Id, "b", Ct);
            await service.AddSubtaskAsync(task.Id, "c", Ct);
            await service.SetSubtaskCompletedAsync(b.Id, true, Ct);

            var deleted = await service.DeleteSubtaskAsync(b.Id, Ct);
            Assert.Equal(["a", "c"], (await SubtasksOfAsync(service, task.Id)).Select(s => s.Title));

            await service.RestoreSubtaskAsync(deleted, Ct);

            var restored = (await SubtasksOfAsync(service, task.Id)).Single(s => s.Id == b.Id);
            Assert.Equal(["a", "b", "c"], (await SubtasksOfAsync(service, task.Id)).Select(s => s.Title));
            Assert.True(restored.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreSubtaskAsync(deleted, Ct));
        }
    }

    [Fact]
    public async Task Restoring_a_Subtask_whose_Task_is_gone_fails()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var task = await service.CreateTaskAsync("trip", Ct);
            var subtask = await service.AddSubtaskAsync(task.Id, "a", Ct);
            var deleted = await service.DeleteSubtaskAsync(subtask.Id, Ct);
            await service.DeleteTaskAsync(task.Id, Ct);

            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RestoreSubtaskAsync(deleted, Ct));
        }
    }

    [Fact]
    public async Task Deleting_a_Task_deletes_its_Subtasks_and_leaves_other_Tasks_alone()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var doomed = await service.CreateTaskAsync("doomed", Ct);
            var kept = await service.CreateTaskAsync("kept", Ct);
            await service.AddSubtaskAsync(doomed.Id, "x", Ct);
            await service.AddSubtaskAsync(kept.Id, "y", Ct);

            await service.DeleteTaskAsync(doomed.Id, Ct);

            var remaining = await db.QueryAsync(c => c.Subtasks);
            Assert.Equal(["y"], remaining.Select(s => s.Title));
        }
    }

    [Fact]
    public async Task Deleting_a_List_cascades_to_the_Subtasks_of_its_Tasks()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var lists = db.Get<IListService>();
            var work = await lists.CreateListAsync("Work", Ct);
            await db.AddTaskAsync(work.Id, "in work", completed: false);
            var inWork = (await db.QueryAsync(c => c.Tasks.Where(t => t.ListId == work.Id))).Single();
            var inbox = await service.CreateTaskAsync("in inbox", Ct);
            await service.AddSubtaskAsync(inWork.Id, "gone", Ct);
            await service.AddSubtaskAsync(inbox.Id, "stays", Ct);

            await lists.DeleteListAsync(work.Id, Ct);

            Assert.Equal(["stays"], (await db.QueryAsync(c => c.Subtasks)).Select(s => s.Title));
        }
    }

    [Fact]
    public async Task Undoing_a_Task_delete_restores_its_Subtasks_with_the_same_ids_state_and_order()
    {
        var (db, service) = await StartAsync();
        using (db)
        {
            var task = await service.CreateTaskAsync("trip", Ct);
            var first = await service.AddSubtaskAsync(task.Id, "a", Ct);
            await service.AddSubtaskAsync(task.Id, "b", Ct);
            var third = await service.AddSubtaskAsync(task.Id, "c", Ct);
            await service.SetSubtaskCompletedAsync(third.Id, true, Ct);
            await service.MoveSubtaskAsync(third.Id, 0, Ct);

            var snapshot = await service.DeleteTaskAsync(task.Id, Ct);
            Assert.Equal(3, snapshot.Subtasks.Count);
            Assert.Empty(await db.QueryAsync(c => c.Subtasks));

            var restored = await service.RestoreTaskAsync(snapshot, Ct);

            Assert.Equal(["c", "a", "b"], restored.Subtasks.Select(s => s.Title));
            var stored = (await SubtasksOfAsync(service, task.Id)).ToList();
            Assert.Equal(["c", "a", "b"], stored.Select(s => s.Title));
            Assert.Equal([true, false, false], stored.Select(s => s.IsCompleted));
            Assert.Equal(third.Id, stored[0].Id);
            Assert.Equal(first.Id, stored[1].Id);
            Assert.All(stored, s => Assert.Equal(task.Id, s.TaskId));
        }
    }

    [Fact]
    public async Task Subtasks_come_back_with_Tag_filtered_Tasks_and_survive_a_restart()
    {
        using var first = new TempDatabase();
        await first.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        var tasks = first.Get<ITaskService>();
        var task = await tasks.CreateTaskAsync("trip", Ct);
        await tasks.AddSubtaskAsync(task.Id, "a", Ct);
        var tag = await first.Get<ITagService>().AddTagToTaskAsync(task.Id, "travel", Ct);

        var filtered = Assert.Single(await first.Get<ITagService>().ListTasksByTagAsync(tag.Id, Ct));
        Assert.Equal(["a"], filtered.Subtasks.Select(s => s.Title));

        using var second = new TempDatabase(first.Paths);
        await second.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        var reloaded = Assert.Single(await second.Get<ITaskService>().ListDefaultListTasksAsync(Ct));
        Assert.Equal(["a"], reloaded.Subtasks.Select(s => s.Title));
    }
}
