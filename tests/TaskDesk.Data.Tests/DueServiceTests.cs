using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public class DueServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Local time is UTC-3. The shared clock starts at 2026-10-16 09:30Z; move it to local 2026-10-20 10:00.
    private static async Task<TempDatabase> StartAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        db.Clock.SetLocalTimeZone(
            TimeZoneInfo.CreateCustomTimeZone("Test-3", TimeSpan.FromHours(-3), "Test-3", "Test-3"));
        db.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        return db;
    }

    private static DateOnly Day(int day) => new(2026, 10, day);

    private static async Task<TaskItem> AddAsync(
        TempDatabase db, string title, DateOnly? due, TimeOnly? time = null, Guid? listId = null)
    {
        var task = await db.Get<ITaskCreator>().CreateTaskInListAsync(
            title, listId ?? (await db.Get<IListService>().GetListsAsync(Ct)).Single(l => l.List.IsDefault).List.Id,
            due, Ct);
        return time is null ? task : await db.Get<IDueService>().SetDueAsync(task.Id, due, time, Ct);
    }

    private static async Task<List<string>> TitlesAsync(TempDatabase db, SmartView view, int skip = 0) =>
        [.. (await db.Get<IDueService>().ListSmartViewAsync(view, skip, Ct)).Select(t => t.Title)];

    [Fact]
    public async Task Due_date_and_time_are_stored_and_read_back_as_calendar_values()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", null);

        await db.Get<IDueService>().SetDueAsync(task.Id, Day(24), new TimeOnly(14, 30), Ct);

        var stored = Assert.Single(await db.Get<ITaskService>().ListDefaultListTasksAsync(Ct));
        Assert.Equal(Day(24), stored.DueDate);
        Assert.Equal(new TimeOnly(14, 30), stored.DueTime);
    }

    [Fact]
    public async Task Due_can_be_cleared()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(24), new TimeOnly(9, 0));

        await db.Get<IDueService>().SetDueAsync(task.Id, null, null, Ct);

        var stored = Assert.Single(await db.Get<ITaskService>().ListDefaultListTasksAsync(Ct));
        Assert.Null(stored.DueDate);
        Assert.Null(stored.DueTime);
    }

    [Fact]
    public async Task A_Due_time_without_a_Due_date_is_rejected_and_nothing_is_saved()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(24));

        await Assert.ThrowsAsync<ArgumentException>(
            () => db.Get<IDueService>().SetDueAsync(task.Id, null, new TimeOnly(9, 0), Ct));

        var stored = Assert.Single(await db.Get<ITaskService>().ListDefaultListTasksAsync(Ct));
        Assert.Equal(Day(24), stored.DueDate);
    }

    [Fact]
    public async Task Setting_the_Due_of_an_unknown_Task_is_rejected()
    {
        using var db = await StartAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => db.Get<IDueService>().SetDueAsync(Guid.NewGuid(), Day(24), null, Ct));
    }

    [Fact]
    public async Task Due_survives_delete_and_undo_together_with_Tags_and_Subtasks()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", Day(24), new TimeOnly(8, 15));
        var service = db.Get<ITaskService>();
        await db.Get<ITagService>().AddTagToTaskAsync(task.Id, "work", Ct);
        await service.AddSubtaskAsync(task.Id, "step", Ct);

        var snapshot = await service.DeleteTaskAsync(task.Id, Ct);
        var restored = await service.RestoreTaskAsync(snapshot, Ct);

        Assert.Equal(Day(24), restored.DueDate);
        Assert.Equal(new TimeOnly(8, 15), restored.DueTime);
        var stored = Assert.Single(await service.ListDefaultListTasksAsync(Ct));
        Assert.Equal(Day(24), stored.DueDate);
        Assert.Equal(new TimeOnly(8, 15), stored.DueTime);
        Assert.Equal(["work"], stored.Tags.Select(t => t.Name));
        Assert.Equal(["step"], stored.Subtasks.Select(s => s.Title));
    }

    [Fact]
    public async Task Setting_the_Due_returns_the_Task_with_its_Tags_and_Subtasks()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "a", null);
        await db.Get<ITagService>().AddTagToTaskAsync(task.Id, "work", Ct);
        await db.Get<ITaskService>().AddSubtaskAsync(task.Id, "step", Ct);

        var updated = await db.Get<IDueService>().SetDueAsync(task.Id, Day(20), null, Ct);

        Assert.Equal(["work"], updated.Tags.Select(t => t.Name));
        Assert.Equal(["step"], updated.Subtasks.Select(s => s.Title));
    }

    [Theory]
    [InlineData(SmartView.Today)]
    [InlineData(SmartView.Upcoming)]
    [InlineData(SmartView.Overdue)]
    [InlineData(SmartView.All)]
    [InlineData(SmartView.Completed)]
    public async Task Smart_Views_return_Tasks_with_their_Tags_and_Subtasks(SmartView view)
    {
        using var db = await StartAsync(); // local now: 2026-10-20 10:00
        var due = view == SmartView.Upcoming ? Day(22) : Day(19);
        var task = await AddAsync(db, "a", due);
        var service = db.Get<ITaskService>();
        await db.Get<ITagService>().AddTagToTaskAsync(task.Id, "work", Ct);
        await service.AddSubtaskAsync(task.Id, "second", Ct);
        var first = await service.AddSubtaskAsync(task.Id, "first", Ct);
        await service.MoveSubtaskAsync(first.Id, 0, Ct);
        if (view == SmartView.Completed)
        {
            await service.SetCompletedAsync(task.Id, true, Ct);
        }

        var listed = Assert.Single(await db.Get<IDueService>().ListSmartViewAsync(view, 0, Ct));

        Assert.Equal(["work"], listed.Tags.Select(t => t.Name));
        Assert.Equal(["first", "second"], listed.Subtasks.Select(s => s.Title));
    }

    [Fact]
    public async Task Quick_add_with_a_Due_date_persists_it()
    {
        using var db = await StartAsync();
        var work = await db.Get<IListService>().CreateListAsync("Work", Ct);

        var created = await db.Get<ITaskCreator>().CreateTaskInListAsync("Call Ana", work.Id, Day(22), Ct);

        Assert.Equal(Day(22), created.DueDate);
        Assert.Null(created.DueTime);
        var stored = (await db.QueryAsync(c => c.Tasks.Where(t => t.Id == created.Id))).Single();
        Assert.Equal(Day(22), stored.DueDate);
    }

    [Fact]
    public async Task Quick_add_without_a_Due_date_leaves_it_empty()
    {
        using var db = await StartAsync();
        var work = await db.Get<IListService>().CreateListAsync("Work", Ct);

        var created = await db.Get<ITaskCreator>().CreateTaskInListAsync("Call Ana", work.Id, null, Ct);

        Assert.Null(created.DueDate);
    }

    [Fact]
    public async Task Today_holds_Tasks_due_today_or_earlier_sorted_by_Due_then_priority()
    {
        using var db = await StartAsync(); // local now: 2026-10-20 10:00
        await AddAsync(db, "tomorrow", Day(21));
        await AddAsync(db, "no due", null);
        await AddAsync(db, "today 15:00", Day(20), new TimeOnly(15, 0));
        await AddAsync(db, "today date-only", Day(20));
        await AddAsync(db, "yesterday", Day(19));
        var done = await AddAsync(db, "done", Day(19));
        await db.Get<ITaskService>().SetCompletedAsync(done.Id, true, Ct);

        Assert.Equal(["yesterday", "today date-only", "today 15:00"], await TitlesAsync(db, SmartView.Today));
    }

    [Fact]
    public async Task Today_breaks_ties_by_descending_priority()
    {
        using var db = await StartAsync();
        var low = await AddAsync(db, "low", Day(20));
        var high = await AddAsync(db, "high", Day(20));
        var service = db.Get<ITaskService>();
        await service.UpdateTaskAsync(low.Id, "low", null, Priority.Low, Ct);
        await service.UpdateTaskAsync(high.Id, "high", null, Priority.High, Ct);

        Assert.Equal(["high", "low"], await TitlesAsync(db, SmartView.Today));
    }

    [Fact]
    public async Task Overdue_follows_the_single_predicate_at_the_Due_time_boundary()
    {
        using var db = await StartAsync(); // local now: 2026-10-20 10:00
        await AddAsync(db, "timed, passed", Day(20), new TimeOnly(9, 59));
        await AddAsync(db, "timed, exactly now", Day(20), new TimeOnly(10, 0));
        await AddAsync(db, "timed, later", Day(20), new TimeOnly(10, 1));
        await AddAsync(db, "date-only today", Day(20));
        await AddAsync(db, "date-only yesterday", Day(19));

        Assert.Equal(["date-only yesterday", "timed, passed"], await TitlesAsync(db, SmartView.Overdue));
    }

    [Fact]
    public async Task A_date_only_Task_becomes_overdue_at_local_midnight()
    {
        using var db = await StartAsync();
        await AddAsync(db, "date-only", Day(20));

        db.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 21, 2, 59, 59, TimeSpan.Zero)); // 23:59:59 local
        Assert.Empty(await TitlesAsync(db, SmartView.Overdue));

        db.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 21, 3, 0, 0, TimeSpan.Zero)); // 00:00 local
        Assert.Equal(["date-only"], await TitlesAsync(db, SmartView.Overdue));
    }

    [Fact]
    public async Task Today_and_Overdue_agree_on_which_Tasks_are_overdue()
    {
        using var db = await StartAsync();
        await AddAsync(db, "a", Day(20), new TimeOnly(9, 0));
        await AddAsync(db, "b", Day(20), new TimeOnly(11, 0));
        await AddAsync(db, "c", Day(19));
        await AddAsync(db, "d", Day(20));
        var now = db.Clock.GetUtcNow();
        var zone = db.Clock.LocalTimeZone;

        var today = await db.Get<IDueService>().ListSmartViewAsync(SmartView.Today, 0, Ct);
        var overdueInToday = today
            .Where(t => DueCalculator.IsOverdue(t.DueDate, t.DueTime, t.IsCompleted, now, zone))
            .Select(t => t.Title);

        Assert.Equal(overdueInToday, await TitlesAsync(db, SmartView.Overdue));
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(21, true)]
    [InlineData(27, true)]
    [InlineData(28, false)]
    public async Task Upcoming_is_after_today_up_to_today_plus_seven(int dueDay, bool expected)
    {
        using var db = await StartAsync();
        await AddAsync(db, "t", Day(dueDay));

        Assert.Equal(expected, (await TitlesAsync(db, SmartView.Upcoming)).Count == 1);
    }

    [Fact]
    public async Task Upcoming_is_sorted_by_Due_date_and_time()
    {
        using var db = await StartAsync();
        await AddAsync(db, "later", Day(23));
        await AddAsync(db, "sooner 18:00", Day(22), new TimeOnly(18, 0));
        await AddAsync(db, "sooner date-only", Day(22));

        Assert.Equal(["sooner date-only", "sooner 18:00", "later"], await TitlesAsync(db, SmartView.Upcoming));
    }

    [Fact]
    public async Task An_undated_Task_is_only_in_All()
    {
        using var db = await StartAsync();
        await AddAsync(db, "undated", null);

        Assert.Empty(await TitlesAsync(db, SmartView.Today));
        Assert.Empty(await TitlesAsync(db, SmartView.Upcoming));
        Assert.Empty(await TitlesAsync(db, SmartView.Overdue));
        Assert.Equal(["undated"], await TitlesAsync(db, SmartView.All));
    }

    [Fact]
    public async Task All_holds_open_Tasks_across_Lists_ordered_by_List_then_manual_order()
    {
        using var db = await StartAsync();
        var inbox = (await db.Get<IListService>().GetListsAsync(Ct)).Single(l => l.List.IsDefault).List;
        var work = await db.Get<IListService>().CreateListAsync("Work", Ct);
        await AddAsync(db, "work 1", null, listId: work.Id);
        await AddAsync(db, "inbox 1", null, listId: inbox.Id);
        await AddAsync(db, "inbox 2", null, listId: inbox.Id);
        var done = await AddAsync(db, "done", null, listId: work.Id);
        await db.Get<ITaskService>().SetCompletedAsync(done.Id, true, Ct);

        Assert.Equal(["inbox 1", "inbox 2", "work 1"], await TitlesAsync(db, SmartView.All));
    }

    [Fact]
    public async Task Completed_is_newest_first_and_paged_by_100()
    {
        using var db = await StartAsync();
        var service = db.Get<ITaskService>();
        for (var i = 0; i < 101; i++)
        {
            var task = await AddAsync(db, $"t{i:000}", null);
            db.Clock.Advance(TimeSpan.FromMinutes(1));
            await service.SetCompletedAsync(task.Id, true, Ct);
        }

        var first = await TitlesAsync(db, SmartView.Completed);
        var second = await TitlesAsync(db, SmartView.Completed, skip: 100);

        Assert.Equal(100, first.Count);
        Assert.Equal("t100", first[0]);
        Assert.Equal("t001", first[^1]);
        Assert.Equal(["t000"], second);
    }

    [Fact]
    public async Task Collection_includes_load_with_split_queries()
    {
        using var db = await StartAsync();
        await using var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(Ct);

        var options = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());

        Assert.Equal(QuerySplittingBehavior.SplitQuery, options.QuerySplittingBehavior);
    }

    [Fact]
    public async Task Completed_pages_stay_in_a_stable_order_with_their_Tags_and_Subtasks_when_completed_at_once()
    {
        using var db = await StartAsync();
        var service = db.Get<ITaskService>();
        var expected = new List<Guid>();
        for (var i = 0; i < 150; i++)
        {
            var task = await AddAsync(db, $"t{i:000}", null);
            await db.Get<ITagService>().AddTagToTaskAsync(task.Id, "x", Ct);
            await db.Get<ITagService>().AddTagToTaskAsync(task.Id, "y", Ct);
            await service.AddSubtaskAsync(task.Id, "one", Ct);
            await service.AddSubtaskAsync(task.Id, "two", Ct);
            await service.SetCompletedAsync(task.Id, true, Ct); // same clock: every CompletedUtc ties
            expected.Add(task.Id);
        }

        var first = await db.Get<IDueService>().ListSmartViewAsync(SmartView.Completed, 0, Ct);
        var second = await db.Get<IDueService>().ListSmartViewAsync(SmartView.Completed, 100, Ct);
        var again = await db.Get<IDueService>().ListSmartViewAsync(SmartView.Completed, 0, Ct);

        Assert.Equal(100, first.Count);
        Assert.Equal(50, second.Count);
        Assert.Equal(first.Select(t => t.Id), again.Select(t => t.Id));
        Assert.Equal(expected.Order(), first.Concat(second).Select(t => t.Id).Order());
        Assert.All(first.Concat(second), t =>
        {
            Assert.Equal(["x", "y"], t.Tags.Select(g => g.Name));
            Assert.Equal(["one", "two"], t.Subtasks.Select(s => s.Title));
        });
    }

    [Fact]
    public async Task Completed_Tasks_are_in_no_Due_based_view()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "done", Day(19));
        await db.Get<ITaskService>().SetCompletedAsync(task.Id, true, Ct);

        Assert.Empty(await TitlesAsync(db, SmartView.Today));
        Assert.Empty(await TitlesAsync(db, SmartView.Overdue));
        Assert.Empty(await TitlesAsync(db, SmartView.All));
        Assert.Equal(["done"], await TitlesAsync(db, SmartView.Completed));
    }
}
