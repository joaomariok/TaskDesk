using TaskDesk.Core.Lists;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public class ListServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(TempDatabase Db, IListService Lists, ITaskService Tasks)> StartAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return (db, db.Get<IListService>(), db.Get<ITaskService>());
    }

    private static async Task<Guid> DefaultIdAsync(TempDatabase db)
        => (await db.QueryAsync(c => c.Lists.Where(l => l.IsDefault))).Single().Id;

    [Fact]
    public async Task Created_lists_are_trimmed_and_appended_in_order()
    {
        var (db, lists, _) = await StartAsync();
        using (db)
        {
            await lists.CreateListAsync("  Work ", Ct);
            await lists.CreateListAsync("Home", Ct);

            var names = (await lists.GetListsAsync(Ct)).Select(s => s.List.Name);
            Assert.Equal(["Inbox", "Work", "Home"], names);
        }
    }

    [Theory]
    [InlineData("work")]
    [InlineData("WORK")]
    [InlineData("  Work  ")]
    public async Task Names_are_unique_ignoring_case_and_surrounding_spaces(string duplicate)
    {
        var (db, lists, _) = await StartAsync();
        using (db)
        {
            await lists.CreateListAsync("Work", Ct);

            await Assert.ThrowsAsync<DuplicateListNameException>(() => lists.CreateListAsync(duplicate, Ct));

            Assert.Equal(2, (await lists.GetListsAsync(Ct)).Count);
        }
    }

    [Fact]
    public async Task Renaming_to_another_lists_name_in_different_case_is_rejected()
    {
        var (db, lists, _) = await StartAsync();
        using (db)
        {
            await lists.CreateListAsync("Work", Ct);
            var home = await lists.CreateListAsync("Home", Ct);

            await Assert.ThrowsAsync<DuplicateListNameException>(() => lists.RenameListAsync(home.Id, "wORK", Ct));
        }
    }

    [Fact]
    public async Task A_list_can_change_only_the_case_of_its_own_name()
    {
        var (db, lists, _) = await StartAsync();
        using (db)
        {
            var work = await lists.CreateListAsync("work", Ct);

            await lists.RenameListAsync(work.Id, " Work ", Ct);

            Assert.Equal("Work", (await lists.GetListsAsync(Ct)).Single(s => s.List.Id == work.Id).List.Name);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_names_are_rejected(string name)
    {
        var (db, lists, _) = await StartAsync();
        using (db)
        {
            var inbox = await DefaultIdAsync(db);

            await Assert.ThrowsAsync<ArgumentException>(() => lists.CreateListAsync(name, Ct));
            await Assert.ThrowsAsync<ArgumentException>(() => lists.RenameListAsync(inbox, name, Ct));
        }
    }

    [Fact]
    public async Task Name_over_100_characters_is_rejected()
    {
        var (db, lists, _) = await StartAsync();
        using (db)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => lists.CreateListAsync(new string('a', 101), Ct));
            await lists.CreateListAsync(new string('a', 100), Ct);
        }
    }

    [Fact]
    public async Task Colour_can_be_set_and_cleared_and_must_be_hex()
    {
        var (db, lists, _) = await StartAsync();
        using (db)
        {
            var work = await lists.CreateListAsync("Work", Ct);

            await lists.SetListColorAsync(work.Id, "#ff8800", Ct);
            Assert.Equal("#FF8800", (await lists.GetListsAsync(Ct)).Single(s => s.List.Id == work.Id).List.ColorHex);

            await Assert.ThrowsAsync<ArgumentException>(() => lists.SetListColorAsync(work.Id, "orange", Ct));

            await lists.SetListColorAsync(work.Id, null, Ct);
            Assert.Null((await lists.GetListsAsync(Ct)).Single(s => s.List.Id == work.Id).List.ColorHex);
        }
    }

    [Fact]
    public async Task Reorder_persists_the_new_order()
    {
        var (db, lists, _) = await StartAsync();
        using (db)
        {
            var work = await lists.CreateListAsync("Work", Ct);
            var home = await lists.CreateListAsync("Home", Ct);
            var inbox = await DefaultIdAsync(db);

            await lists.ReorderListsAsync([home.Id, inbox, work.Id], Ct);

            Assert.Equal(["Home", "Inbox", "Work"], (await lists.GetListsAsync(Ct)).Select(s => s.List.Name));
        }
    }

    [Fact]
    public async Task Reorder_requires_exactly_the_existing_lists()
    {
        var (db, lists, _) = await StartAsync();
        using (db)
        {
            var work = await lists.CreateListAsync("Work", Ct);
            var inbox = await DefaultIdAsync(db);

            await Assert.ThrowsAsync<ArgumentException>(() => lists.ReorderListsAsync([work.Id], Ct));
            await Assert.ThrowsAsync<ArgumentException>(() => lists.ReorderListsAsync([work.Id, work.Id, inbox], Ct));
            await Assert.ThrowsAsync<ArgumentException>(() => lists.ReorderListsAsync([work.Id, Guid.NewGuid()], Ct));
        }
    }

    [Fact]
    public async Task Open_task_count_ignores_completed_tasks_and_other_lists()
    {
        var (db, lists, tasks) = await StartAsync();
        using (db)
        {
            var work = await lists.CreateListAsync("Work", Ct);
            await tasks.CreateTaskAsync("a", Ct);
            var done = await tasks.CreateTaskAsync("b", Ct);
            await db.AddTaskAsync(work.Id, "w1", completed: false);
            await db.AddTaskAsync(work.Id, "w2", completed: true);
            await db.CompleteTaskAsync(done.Id);

            var counts = (await lists.GetListsAsync(Ct)).ToDictionary(s => s.List.Name, s => s.OpenTaskCount);
            Assert.Equal(1, counts["Inbox"]);
            Assert.Equal(1, counts["Work"]);
        }
    }

    [Fact]
    public async Task Deleting_a_list_deletes_its_tasks_but_not_other_lists_tasks()
    {
        var (db, lists, tasks) = await StartAsync();
        using (db)
        {
            var work = await lists.CreateListAsync("Work", Ct);
            await tasks.CreateTaskAsync("keep", Ct);
            await db.AddTaskAsync(work.Id, "gone 1", completed: false);
            await db.AddTaskAsync(work.Id, "gone 2", completed: true);

            await lists.DeleteListAsync(work.Id, Ct);

            Assert.Equal(["Inbox"], (await lists.GetListsAsync(Ct)).Select(s => s.List.Name));
            Assert.Equal(["keep"], (await db.QueryAsync(c => c.Tasks)).Select(t => t.Title));
        }
    }

    [Fact]
    public async Task The_Default_List_cannot_be_deleted()
    {
        var (db, lists, _) = await StartAsync();
        using (db)
        {
            var inbox = await DefaultIdAsync(db);

            await Assert.ThrowsAsync<DefaultListDeletionException>(() => lists.DeleteListAsync(inbox, Ct));

            Assert.Single(await lists.GetListsAsync(Ct));
        }
    }

    [Fact]
    public async Task The_Default_List_can_be_renamed_and_stays_the_only_default_after_other_changes()
    {
        var (db, lists, _) = await StartAsync();
        using (db)
        {
            var inbox = await DefaultIdAsync(db);
            var work = await lists.CreateListAsync("Work", Ct);

            await lists.RenameListAsync(inbox, "Everything", Ct);
            await lists.SetListColorAsync(inbox, "#112233", Ct);
            await lists.ReorderListsAsync([work.Id, inbox], Ct);
            await lists.DeleteListAsync(work.Id, Ct);

            var only = Assert.Single(await lists.GetListsAsync(Ct)).List;
            Assert.True(only.IsDefault);
            Assert.Equal(inbox, only.Id);
            Assert.Equal("Everything", only.Name);
            Assert.Single(await db.QueryAsync(c => c.Lists.Where(l => l.IsDefault)));
        }
    }

    [Fact]
    public async Task Unknown_list_ids_are_reported()
    {
        var (db, lists, _) = await StartAsync();
        using (db)
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => lists.RenameListAsync(Guid.NewGuid(), "x", Ct));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => lists.SetListColorAsync(Guid.NewGuid(), null, Ct));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => lists.DeleteListAsync(Guid.NewGuid(), Ct));
        }
    }
}
