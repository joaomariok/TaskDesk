using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using FakeDialogs = TaskDesk.Presentation.Tests.SidebarViewModelTests.FakeDialogs;
using FakeListService = TaskDesk.Presentation.Tests.SidebarViewModelTests.FakeListService;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

// The sidebar shows the main view model's page as its selected entry, and opens the entry the owner picks.
public class SidebarSelectionTests
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

    // Finds nothing; the search and Filters only need to run.
    private sealed class EmptySearchService : ITaskSearchService
    {
        public Task<IReadOnlyList<TaskItem>> SearchAsync(
            TaskSearchQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>([]);
    }

    // Every listing returns new Tag instances, as a database does on each reload.
    private sealed class CopyingTagService(FakeTagService inner) : ITagService
    {
        public async Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken cancellationToken = default)
            => [.. (await inner.ListTagsAsync(cancellationToken)).Select(t => new Tag
            {
                Id = t.Id,
                Name = t.Name,
                NormalizedName = t.NormalizedName,
                ColorHex = t.ColorHex,
            })];

        public Task<Tag> CreateTagAsync(string name, CancellationToken cancellationToken = default)
            => inner.CreateTagAsync(name, cancellationToken);

        public Task<Tag> AddTagToTaskAsync(Guid taskId, string name, CancellationToken cancellationToken = default)
            => inner.AddTagToTaskAsync(taskId, name, cancellationToken);

        public Task RemoveTagFromTaskAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default)
            => inner.RemoveTagFromTaskAsync(taskId, tagId, cancellationToken);

        public Task DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default)
            => inner.DeleteTagAsync(tagId, cancellationToken);

        public Task<IReadOnlyList<TaskItem>> ListTasksByTagAsync(Guid tagId, CancellationToken cancellationToken = default)
            => inner.ListTasksByTagAsync(tagId, cancellationToken);
    }

    private sealed record Setup(
        FakeListService ListService, MainViewModel Main, SidebarViewModel Sidebar, TaskList Work, Tag Home);

    // "Inbox" (the Default List) and "Work", a Tag "home", and both view models loaded unless asked otherwise.
    private static async Task<Setup> StartAsync(bool loadMain = true)
    {
        var listService = new FakeListService();
        var work = await listService.CreateListAsync("Work", Ct);
        var tasks = new FakeTaskService();
        var tags = new FakeTagService(tasks);
        var home = await tags.CreateTagAsync("home", Ct);
        var main = new MainViewModel(
            tasks, new CopyingTagService(tags), new FakeTimeProvider(), new EmptyDueService(), new EmptySearchService(),
            listService: listService);
        var sidebar = new SidebarViewModel(main, listService, new FakeDialogs());
        await sidebar.LoadAsync(Ct);
        if (loadMain)
        {
            await main.LoadAsync(Ct);
        }

        return new Setup(listService, main, sidebar, work, home);
    }

    [Fact]
    public async Task A_Smart_View_page_selects_that_Smart_View_and_no_List_or_Tag()
    {
        var s = await StartAsync();

        await s.Main.ShowSmartViewAsync(SmartView.Upcoming, Ct);

        Assert.Equal(SmartView.Upcoming, s.Sidebar.SelectedSmartView);
        Assert.Null(s.Sidebar.SelectedList);
        Assert.Null(s.Sidebar.SelectedTag);
    }

    [Fact]
    public async Task A_List_page_selects_that_List_and_no_Smart_View_or_Tag()
    {
        var s = await StartAsync();

        await s.Main.ShowListAsync(s.Work.Id, Ct);

        Assert.Same(s.Sidebar.Lists.Single(l => l.Name == "Work"), s.Sidebar.SelectedList);
        Assert.Null(s.Sidebar.SelectedSmartView);
        Assert.Null(s.Sidebar.SelectedTag);
    }

    [Fact]
    public async Task A_Tag_page_selects_the_current_instance_of_that_Tag_and_no_Smart_View_or_List()
    {
        var s = await StartAsync();

        await s.Main.FilterByTagAsync(s.Main.Tags.Single(), Ct);

        Assert.Same(s.Main.Tags.Single(), s.Sidebar.SelectedTag);
        Assert.Null(s.Sidebar.SelectedSmartView);
        Assert.Null(s.Sidebar.SelectedList);
    }

    [Fact]
    public async Task The_placeholder_page_before_the_first_load_selects_the_Default_List()
    {
        var s = await StartAsync(loadMain: false);

        Assert.Null(s.Main.CurrentListId);
        Assert.Same(s.Sidebar.Lists.Single(l => l.IsDefault), s.Sidebar.SelectedList);
    }

    [Fact]
    public async Task A_page_on_a_deleted_List_selects_the_Default_List_once_the_Lists_reload()
    {
        var s = await StartAsync();
        await s.Main.ShowListAsync(s.Work.Id, Ct);

        await s.ListService.DeleteListAsync(s.Work.Id, Ct);
        await s.Sidebar.LoadAsync(Ct); // the page still names the deleted List

        Assert.Equal(s.Work.Id, s.Main.CurrentListId);
        Assert.Same(s.Sidebar.Lists.Single(l => l.IsDefault), s.Sidebar.SelectedList);
        Assert.Same(s.Sidebar.SelectedList, s.Sidebar.ListOrDefault(s.Main.CurrentListId));
    }

    [Fact]
    public async Task After_a_reload_the_selected_Tag_is_the_reloaded_instance()
    {
        var s = await StartAsync();
        var before = s.Main.Tags.Single();
        await s.Main.FilterByTagAsync(before, Ct);

        await s.Main.LoadAsync(Ct);

        var reloaded = s.Main.Tags.Single();
        Assert.NotSame(before, reloaded);
        Assert.Same(reloaded, s.Sidebar.SelectedTag);
    }

    [Fact]
    public async Task After_a_Tag_reload_the_selected_Tag_is_the_new_instance_before_the_page_follows()
    {
        var s = await StartAsync();
        await s.Main.FilterByTagAsync(s.Main.Tags.Single(), Ct);
        var shown = s.Main.TagFilter!;
        var fresh = new Tag { Id = shown.Id, Name = shown.Name, NormalizedName = shown.NormalizedName };

        s.Main.Tags.Clear();
        s.Main.Tags.Add(fresh);

        Assert.Same(shown, s.Main.TagFilter);
        Assert.Same(fresh, s.Sidebar.SelectedTag);
    }

    [Fact]
    public async Task A_page_change_announces_the_selection()
    {
        var s = await StartAsync();
        var changed = new List<string?>();
        s.Sidebar.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await s.Main.ShowSmartViewAsync(SmartView.All, Ct);

        Assert.Contains(nameof(SidebarViewModel.SelectedSmartView), changed);
        Assert.Contains(nameof(SidebarViewModel.SelectedList), changed);
        Assert.Contains(nameof(SidebarViewModel.SelectedTag), changed);
    }

    [Fact]
    public async Task A_Lists_reload_announces_the_selected_List()
    {
        var s = await StartAsync();
        var changed = new List<string?>();
        s.Sidebar.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await s.Sidebar.LoadAsync(Ct);

        Assert.Contains(nameof(SidebarViewModel.SelectedList), changed);
    }

    [Fact]
    public async Task Opening_a_Smart_View_shows_its_page()
    {
        var s = await StartAsync();

        await s.Sidebar.OpenAsync(new SidebarEntry.SmartViewEntry(SmartView.Overdue), Ct);

        Assert.Equal(SmartView.Overdue, s.Main.CurrentView);
        Assert.Equal(SmartView.Overdue, s.Sidebar.SelectedSmartView);
    }

    [Fact]
    public async Task Opening_a_Tag_shows_its_page()
    {
        var s = await StartAsync();

        await s.Sidebar.OpenAsync(new SidebarEntry.TagEntry(s.Main.Tags.Single()), Ct);

        Assert.Equal(s.Home.Id, s.Main.TagFilter?.Id);
        Assert.Same(s.Main.Tags.Single(), s.Sidebar.SelectedTag);
    }

    [Fact]
    public async Task Opening_a_List_shows_its_page_and_ends_the_search_but_keeps_the_Filters()
    {
        var s = await StartAsync();
        await s.Main.ToggleHasDueFilterCommand.ExecuteAsync(null);
        s.Main.SearchText = "report";

        await s.Sidebar.OpenAsync(new SidebarEntry.ListEntry(s.Work.Id), Ct);

        Assert.Equal(s.Work.Id, s.Main.CurrentListId);
        Assert.Same(s.Sidebar.Lists.Single(l => l.Name == "Work"), s.Sidebar.SelectedList);
        Assert.Equal("", s.Main.SearchText);
        Assert.True(s.Main.HasDueFilter);
    }

    [Fact]
    public async Task Opening_the_List_already_selected_opens_it_again_and_ends_the_search()
    {
        var s = await StartAsync();
        await s.Sidebar.OpenAsync(new SidebarEntry.ListEntry(s.Work.Id), Ct);
        s.Main.SearchText = "report";

        await s.Sidebar.OpenAsync(new SidebarEntry.ListEntry(s.Work.Id), Ct);

        Assert.Equal("", s.Main.SearchText);
        Assert.Equal(s.Work.Id, s.Main.CurrentListId);
        Assert.False(s.Main.IsNarrowed);
    }

    [Fact]
    public async Task A_sidebar_without_Lists_selects_and_opens_Smart_Views_and_Tags_and_never_a_List()
    {
        var s = await StartAsync();
        var sidebar = SidebarViewModel.WithoutLists(s.Main);
        await sidebar.LoadAsync(Ct);

        Assert.Empty(sidebar.Lists);
        Assert.Null(sidebar.SelectedList);
        await sidebar.OpenAsync(new SidebarEntry.SmartViewEntry(SmartView.All), Ct);
        Assert.Equal(SmartView.All, sidebar.SelectedSmartView);
        await sidebar.OpenAsync(new SidebarEntry.TagEntry(s.Main.Tags.Single()), Ct);
        Assert.Same(s.Main.Tags.Single(), sidebar.SelectedTag);
    }

    [Fact]
    public async Task Opening_the_Smart_View_or_Tag_already_shown_changes_nothing()
    {
        var s = await StartAsync();
        await s.Main.ShowSmartViewAsync(SmartView.Today, Ct);
        s.Main.SearchText = "report";

        await s.Sidebar.OpenAsync(new SidebarEntry.SmartViewEntry(SmartView.Today), Ct);
        Assert.Equal("report", s.Main.SearchText);

        await s.Main.FilterByTagAsync(s.Main.Tags.Single(), Ct);
        var tagPage = s.Main.Page;
        await s.Sidebar.OpenAsync(new SidebarEntry.TagEntry(s.Main.Tags.Single()), Ct);
        Assert.Same(tagPage, s.Main.Page);
    }
}
