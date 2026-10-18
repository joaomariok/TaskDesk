using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tasks;
using FakeListService = TaskDesk.Presentation.Tests.ListsViewModelTests.FakeListService;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

// Every List has its own page: it shows the List's Tasks, is named after it and feeds quick-add.
public class ListPageViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // A Smart View shows no Task at all.
    private sealed class EmptyDueService : IDueService
    {
        public Task<TaskItem> SetDueAsync(
            Guid taskId, DateOnly? dueDate, TimeOnly? dueTime, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SmartViewCounts> CountSmartViewsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TaskItem>> ListSmartViewAsync(
            SmartView view, int skip = 0, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>([]);
    }

    // Matches on the title and the has-due filter, within the List the query names (if any).
    private sealed class ListSearchService(FakeTaskService tasks) : ITaskSearchService
    {
        public List<TaskSearchQuery> Queries { get; } = [];

        public Task<IReadOnlyList<TaskItem>> SearchAsync(
            TaskSearchQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<TaskItem>>([.. tasks.All.Where(t =>
                (query.ViewListId is null || t.ListId == query.ViewListId)
                && (string.IsNullOrWhiteSpace(query.Text)
                    || t.Title.Contains(query.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                && (!query.HasDue || t.DueDate != null))]);
        }
    }

    private sealed record Setup(
        FakeTaskService Tasks, FakeTagService Tags, FakeListService Lists, ListSearchService Search,
        TaskList Inbox, TaskList Work, MainViewModel Vm);

    // "Inbox" (the Default List) with "inbox task", and "Work" with "report".
    private static async Task<Setup> StartAsync()
    {
        var lists = new FakeListService();
        var inbox = (await lists.GetListsAsync(Ct)).Single().List;
        var work = await lists.CreateListAsync("Work", Ct);
        var tasks = new FakeTaskService();
        (await tasks.CreateTaskAsync("inbox task", Ct)).ListId = inbox.Id;
        await tasks.CreateTaskInListAsync("report", work.Id, Ct);
        tasks.CreatedInList.Clear();
        var tags = new FakeTagService(tasks);
        var search = new ListSearchService(tasks);
        var vm = new MainViewModel(
            tasks, tags, new FakeTimeProvider(), dueService: new EmptyDueService(), searchService: search,
            listService: lists, taskCreator: tasks);
        await vm.LoadAsync(Ct);
        return new Setup(tasks, tags, lists, search, inbox, work, vm);
    }

    [Fact]
    public async Task The_first_page_is_the_Default_List_with_its_real_id()
    {
        var s = await StartAsync();

        Assert.Equal(s.Inbox.Id, s.Vm.CurrentListId);
        Assert.True(s.Vm.IsListPage);
        Assert.Equal("Inbox", s.Vm.PageHeading);
        Assert.Equal(["inbox task"], s.Vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Opening_a_List_shows_only_its_Tasks_and_names_the_page_after_it()
    {
        var s = await StartAsync();

        await s.Vm.ShowListAsync(s.Work.Id, Ct);

        Assert.Equal(s.Work.Id, s.Vm.CurrentListId);
        Assert.True(s.Vm.IsListPage);
        Assert.Null(s.Vm.CurrentView);
        Assert.Null(s.Vm.TagFilter);
        Assert.Equal(["report"], s.Vm.Tasks.Select(t => t.Title));
        Assert.Equal("Work", s.Vm.PageHeading);
        Assert.Equal("Search in Work", s.Vm.SearchScope);
        Assert.Equal("Add a task to Work", s.Vm.QuickAddTarget);
    }

    [Fact]
    public async Task An_empty_List_page_names_the_List()
    {
        var s = await StartAsync();
        var home = await s.Lists.CreateListAsync("Home", Ct);

        await s.Vm.ShowListAsync(home.Id, Ct);

        Assert.Equal("No Tasks in Home yet. Type above to add one.", s.Vm.EmptyMessage);
    }

    [Fact]
    public async Task Quick_add_on_a_List_page_adds_to_that_List()
    {
        var s = await StartAsync();
        await s.Vm.ShowListAsync(s.Work.Id, Ct);
        s.Vm.QuickAddTitle = "call Ana";

        await s.Vm.AddTaskCommand.ExecuteAsync(null);

        Assert.Equal([("call Ana", s.Work.Id)], s.Tasks.CreatedInList);
        Assert.Equal(["report", "call Ana"], s.Vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Quick_add_on_a_Smart_View_still_adds_to_the_Default_List()
    {
        var s = await StartAsync();
        await s.Vm.ShowListAsync(s.Work.Id, Ct);
        await s.Vm.ShowSmartViewAsync(SmartView.All, Ct);
        s.Vm.QuickAddTitle = "call Ana";

        Assert.Equal("Add a task to Inbox", s.Vm.QuickAddTarget);
        await s.Vm.AddTaskCommand.ExecuteAsync(null);

        Assert.Empty(s.Tasks.CreatedInList);
        Assert.Contains("call Ana", s.Tasks.Created);
    }

    [Fact]
    public async Task Deleting_the_shown_List_falls_back_to_the_Default_List()
    {
        var s = await StartAsync();
        await s.Vm.ShowListAsync(s.Work.Id, Ct);

        await s.Lists.DeleteListAsync(s.Work.Id, Ct);
        await s.Vm.LoadAsync(Ct);

        Assert.Equal(s.Inbox.Id, s.Vm.CurrentListId);
        Assert.Equal("Inbox", s.Vm.PageHeading);
    }

    [Fact]
    public async Task Renaming_the_shown_List_renames_the_page()
    {
        var s = await StartAsync();
        await s.Vm.ShowListAsync(s.Work.Id, Ct);

        await s.Lists.RenameListAsync(s.Work.Id, "Office", Ct);
        await s.Vm.LoadAsync(Ct);

        Assert.Equal("Office", s.Vm.PageHeading);
        Assert.Equal("Add a task to Office", s.Vm.QuickAddTarget);
    }

    [Fact]
    public async Task Opening_a_List_ends_the_search_and_keeps_the_Filters()
    {
        var s = await StartAsync();
        s.Vm.SearchText = "task";
        s.Vm.HasDueFilter = true;

        await s.Vm.ShowListAsync(s.Work.Id, Ct);

        Assert.Equal("", s.Vm.SearchText);
        Assert.True(s.Vm.HasDueFilter);
        Assert.Equal(s.Work.Id, s.Search.Queries[^1].ViewListId);
    }

    [Fact]
    public async Task Each_page_replaces_the_previous_one()
    {
        var s = await StartAsync();
        var task = s.Tasks.All.Single(t => t.Title == "report");
        await s.Tags.AddTagToTaskAsync(task.Id, "urgent", Ct);
        await s.Vm.LoadAsync(Ct);

        await s.Vm.FilterByTagAsync(s.Vm.Tags.Single(), Ct);
        Assert.Null(s.Vm.CurrentListId);
        Assert.Null(s.Vm.CurrentView);
        Assert.Equal("urgent", s.Vm.TagFilter?.Name);

        await s.Vm.ShowSmartViewAsync(SmartView.Today, Ct);
        Assert.Null(s.Vm.CurrentListId);
        Assert.Equal(SmartView.Today, s.Vm.CurrentView);
        Assert.Null(s.Vm.TagFilter);

        await s.Vm.ShowListAsync(s.Work.Id, Ct);
        Assert.Equal(s.Work.Id, s.Vm.CurrentListId);
        Assert.Null(s.Vm.CurrentView);
        Assert.Null(s.Vm.TagFilter);
    }

    [Fact]
    public async Task Ctrl_N_on_Completed_opens_the_Default_List()
    {
        var s = await StartAsync();
        await s.Vm.ShowListAsync(s.Work.Id, Ct);
        await s.Vm.ShowSmartViewAsync(SmartView.Completed, Ct);

        await s.Vm.FocusQuickAddCommand.ExecuteAsync(null);

        Assert.Equal(s.Inbox.Id, s.Vm.CurrentListId);
    }
}
