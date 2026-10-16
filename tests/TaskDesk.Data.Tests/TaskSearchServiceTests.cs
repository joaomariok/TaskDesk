using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public class TaskSearchServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<TempDatabase> StartAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return db;
    }

    private static async Task<TaskItem> AddAsync(
        TempDatabase db, string title, string? notes = null, Priority priority = Priority.None)
    {
        var task = await db.Get<ITaskService>().CreateTaskAsync(title, Ct);
        if (notes is not null || priority != Priority.None)
        {
            await db.Get<ITaskService>().UpdateTaskAsync(task.Id, title, notes, priority, Ct);
        }

        return task;
    }

    private static async Task<List<string>> SearchAsync(TempDatabase db, TaskSearchQuery query) =>
        [.. (await db.Get<ITaskSearchService>().SearchAsync(query, Ct)).Select(t => t.Title)];

    [Fact]
    public async Task Text_matches_title_notes_and_subtask_titles_ignoring_case()
    {
        using var db = await StartAsync();
        await AddAsync(db, "Buy MILK");
        await AddAsync(db, "Errand", notes: "remember the milk bottle");
        var third = await AddAsync(db, "Chores");
        await db.Get<ITaskService>().AddSubtaskAsync(third.Id, "wash Milk jug", Ct);
        await AddAsync(db, "Unrelated");

        Assert.Equal(["Buy MILK", "Errand", "Chores"], await SearchAsync(db, new(Text: "milk")));
    }

    [Fact]
    public async Task Percent_and_underscore_and_backslash_are_matched_literally()
    {
        using var db = await StartAsync();
        await AddAsync(db, "100% done");
        await AddAsync(db, "1000 done");
        await AddAsync(db, "snake_case");
        await AddAsync(db, "snakeXcase");
        await AddAsync(db, @"C:\temp");

        Assert.Equal(["100% done"], await SearchAsync(db, new(Text: "100%")));
        Assert.Equal(["snake_case"], await SearchAsync(db, new(Text: "e_c")));
        Assert.Equal([@"C:\temp"], await SearchAsync(db, new(Text: @"C:\t")));
        Assert.Equal(["100% done"], await SearchAsync(db, new(Text: "%")));
        Assert.Equal(["snake_case"], await SearchAsync(db, new(Text: "_")));
    }

    [Fact]
    public async Task Blank_text_and_no_filters_return_the_whole_view()
    {
        using var db = await StartAsync();
        await AddAsync(db, "a");
        await AddAsync(db, "b");

        Assert.Equal(["a", "b"], await SearchAsync(db, new(Text: "  ")));
    }

    [Fact]
    public async Task Priority_filter_means_at_least_the_chosen_level()
    {
        using var db = await StartAsync();
        await AddAsync(db, "none");
        await AddAsync(db, "low", priority: Priority.Low);
        await AddAsync(db, "medium", priority: Priority.Medium);
        await AddAsync(db, "high", priority: Priority.High);

        Assert.Equal(["medium", "high"], await SearchAsync(db, new(MinPriority: Priority.Medium)));
    }

    [Fact]
    public async Task Tag_filter_requires_all_tags()
    {
        using var db = await StartAsync();
        var both = await AddAsync(db, "both");
        var onlyWork = await AddAsync(db, "work only");
        await AddAsync(db, "none");
        var tags = db.Get<ITagService>();
        var work = await tags.AddTagToTaskAsync(both.Id, "work", Ct);
        var home = await tags.AddTagToTaskAsync(both.Id, "home", Ct);
        await tags.AddTagToTaskAsync(onlyWork.Id, "work", Ct);

        Assert.Equal(["both", "work only"], await SearchAsync(db, new(TagIds: [work.Id])));
        Assert.Equal(["both"], await SearchAsync(db, new(TagIds: [work.Id, home.Id])));
    }

    [Fact]
    public async Task Has_due_filter_keeps_only_tasks_with_a_due_date()
    {
        using var db = await StartAsync();
        await AddAsync(db, "undated");
        var dated = await AddAsync(db, "dated");
        await db.Get<IDueService>().SetDueAsync(dated.Id, new DateOnly(2026, 10, 20), null, Ct);

        Assert.Equal(["dated"], await SearchAsync(db, new(HasDue: true)));
    }

    [Fact]
    public async Task Conditions_combine_and_results_carry_tags_and_subtasks()
    {
        using var db = await StartAsync();
        var match = await AddAsync(db, "report", priority: Priority.High);
        await AddAsync(db, "report low", priority: Priority.Low);
        await db.Get<ITaskService>().AddSubtaskAsync(match.Id, "draft", Ct);
        var tag = await db.Get<ITagService>().AddTagToTaskAsync(match.Id, "work", Ct);

        var found = Assert.Single(await db.Get<ITaskSearchService>().SearchAsync(
            new(Text: "report", MinPriority: Priority.Medium, TagIds: [tag.Id]), Ct));

        Assert.Equal("report", found.Title);
        Assert.Equal(["work"], found.Tags.Select(t => t.Name));
        Assert.Equal(["draft"], found.Subtasks.Select(s => s.Title));
    }

    [Fact]
    public async Task Results_carry_the_recurrence_with_tags_and_subtasks_in_every_view()
    {
        using var db = await StartAsync();
        var task = await AddAsync(db, "water plants");
        await db.Get<IDueService>().SetDueAsync(task.Id, new DateOnly(2026, 10, 20), null, Ct);
        await db.Get<IRecurrenceService>().SetRecurrenceAsync(task.Id, Recurrence.Weekly(RecurrenceDays.Tuesday), Ct);
        await db.Get<ITaskService>().AddSubtaskAsync(task.Id, "fern", Ct);
        await db.Get<ITagService>().AddTagToTaskAsync(task.Id, "home", Ct);

        foreach (var view in new SmartView?[] { null, SmartView.All })
        {
            var found = Assert.Single(await db.Get<ITaskSearchService>().SearchAsync(
                new(Text: "plants", View: view), Ct));

            Assert.Equal(Recurrence.Weekly(RecurrenceDays.Tuesday), found.Recurrence);
            Assert.Equal(["home"], found.Tags.Select(t => t.Name));
            Assert.Equal(["fern"], found.Subtasks.Select(s => s.Title));
        }
    }

    [Fact]
    public async Task Search_stays_within_the_default_list_or_the_tag_view()
    {
        using var db = await StartAsync();
        var other = await db.Get<IListService>().CreateListAsync("Other", Ct);
        await db.Get<ITaskCreator>().CreateTaskInListAsync("milk in other", other.Id, Ct);
        var inDefault = await AddAsync(db, "milk default");
        var tag = await db.Get<ITagService>().AddTagToTaskAsync(inDefault.Id, "t", Ct);
        await AddAsync(db, "milk untagged");

        Assert.Equal(["milk default", "milk untagged"], await SearchAsync(db, new(Text: "milk")));
        Assert.Equal(["milk default"], await SearchAsync(db, new(Text: "milk", ViewTagId: tag.Id)));
    }

    [Fact]
    public async Task Search_within_smart_views_applies_the_view_and_the_filter()
    {
        using var db = await StartAsync();
        var done = await AddAsync(db, "milk done");
        await AddAsync(db, "milk open");
        await AddAsync(db, "bread open");
        await db.Get<ITaskService>().SetCompletedAsync(done.Id, true, Ct);

        Assert.Equal(["milk open"], await SearchAsync(db, new(Text: "milk", View: SmartView.All)));
        Assert.Equal(["milk done"], await SearchAsync(db, new(Text: "milk", View: SmartView.Completed)));
    }

    [Fact]
    public void A_query_with_text_or_a_filter_is_not_empty()
    {
        Assert.True(new TaskSearchQuery().IsEmpty);
        Assert.True(new TaskSearchQuery(Text: " ").IsEmpty);
        Assert.False(new TaskSearchQuery(HasDue: true).IsEmpty);
        Assert.False(new TaskSearchQuery(MinPriority: Priority.Low).IsEmpty);
        Assert.False(new TaskSearchQuery(TagIds: [Guid.NewGuid()]).IsEmpty);
        Assert.False(new TaskSearchQuery(Text: "x").IsEmpty);
    }
}
