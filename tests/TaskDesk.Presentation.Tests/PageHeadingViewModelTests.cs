using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

public class PageHeadingViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // A Smart View shows no Task at all; the real selection rules are covered by the Due tests.
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

    // Matches on the title and the has-due filter only.
    private sealed class TitleSearchService(FakeTaskService tasks) : ITaskSearchService
    {
        public Task<IReadOnlyList<TaskItem>> SearchAsync(
            TaskSearchQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>([.. tasks.All.Where(t =>
                (string.IsNullOrWhiteSpace(query.Text)
                    || t.Title.Contains(query.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                && (!query.HasDue || t.DueDate != null))]);
    }

    private static (FakeTaskService Service, FakeTagService Tags, MainViewModel Vm) Start()
    {
        var service = new FakeTaskService();
        var tags = new FakeTagService(service);
        var vm = new MainViewModel(
            service, tags, dueService: new EmptyDueService(), searchService: new TitleSearchService(service))
        {
            DefaultListName = "Work",
        };
        return (service, tags, vm);
    }

    [Theory]
    [InlineData(SmartView.Today, "Today", "Nothing due today.")]
    [InlineData(SmartView.Overdue, "Overdue", "Nothing is overdue.")]
    [InlineData(SmartView.Upcoming, "Upcoming", "Nothing is coming up. Tasks with a later Due date appear here.")]
    [InlineData(SmartView.All, "All", "No Tasks yet. Type above to add one.")]
    [InlineData(SmartView.Completed, "Completed", "No completed Tasks.")]
    public async Task Each_Smart_View_has_its_name_and_its_own_empty_message(
        SmartView view, string heading, string empty)
    {
        var (_, _, vm) = Start();

        await vm.ShowSmartViewAsync(view, Ct);

        Assert.Equal(heading, vm.PageHeading);
        Assert.Equal(empty, vm.EmptyMessage);
        Assert.True(vm.HasEmptyMessage);
        Assert.False(vm.IsListPage);
        Assert.False(vm.ShowClearSearch);
    }

    [Theory]
    [InlineData(SmartView.Today, "Search in Today")]
    [InlineData(SmartView.Completed, "Search in Completed")]
    public async Task Search_names_the_Smart_View_it_searches_in(SmartView view, string scope)
    {
        var (_, _, vm) = Start();

        await vm.ShowSmartViewAsync(view, Ct);

        Assert.Equal(scope, vm.SearchScope);
    }

    [Fact]
    public async Task Quick_add_names_the_Default_List_in_every_Smart_View()
    {
        var (_, _, vm) = Start();
        Assert.Equal("Add a task to Work", vm.QuickAddTarget);

        foreach (var view in Enum.GetValues<SmartView>())
        {
            await vm.ShowSmartViewAsync(view, Ct);
            Assert.Equal("Add a task to Work", vm.QuickAddTarget);
        }
    }

    [Fact]
    public async Task A_List_page_shows_the_List_name_and_invites_the_first_Task()
    {
        var (_, _, vm) = Start();

        await vm.LoadAsync(Ct);

        Assert.True(vm.IsListPage);
        Assert.Equal("Work", vm.PageHeading);
        Assert.Equal("Search in Work", vm.SearchScope);
        Assert.Equal("No Tasks in Work yet. Type above to add one.", vm.EmptyMessage);
    }

    [Fact]
    public async Task Renaming_the_List_updates_the_heading_and_the_texts_built_on_it()
    {
        var (_, _, vm) = Start();
        await vm.LoadAsync(Ct);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.DefaultListName = "Home";

        Assert.Equal("Home", vm.PageHeading);
        Assert.Equal("Add a task to Home", vm.QuickAddTarget);
        Assert.Equal("No Tasks in Home yet. Type above to add one.", vm.EmptyMessage);
        Assert.Contains(nameof(MainViewModel.PageHeading), changed);
        Assert.Contains(nameof(MainViewModel.QuickAddTarget), changed);
        Assert.Contains(nameof(MainViewModel.SearchScope), changed);
        Assert.Contains(nameof(MainViewModel.EmptyMessage), changed);
    }

    [Fact]
    public async Task A_Tags_page_is_named_by_the_Tag_and_searches_within_it()
    {
        var (service, tags, vm) = Start();
        var task = await service.CreateTaskAsync("Ship it", Ct);
        await tags.AddTagToTaskAsync(task.Id, "work", Ct);
        await vm.LoadAsync(Ct);
        await vm.FilterByTagAsync(vm.Tags.Single(), Ct);

        Assert.Equal("#work", vm.PageHeading);
        Assert.Equal("Search in #work", vm.SearchScope);
        Assert.False(vm.IsListPage);
        Assert.Equal("", vm.EmptyMessage);
        Assert.False(vm.HasEmptyMessage);
        Assert.Equal("Add a task to Work", vm.QuickAddTarget);
    }

    [Fact]
    public async Task An_empty_Tag_page_says_no_Task_has_the_Tag()
    {
        var (service, tags, vm) = Start();
        var task = await service.CreateTaskAsync("Ship it", Ct);
        await tags.AddTagToTaskAsync(task.Id, "work", Ct);
        await vm.LoadAsync(Ct);
        await vm.FilterByTagAsync(vm.Tags.Single(), Ct);
        await tags.RemoveTagFromTaskAsync(task.Id, vm.Tags.Single().Id, Ct);

        await vm.LoadAsync(Ct);

        Assert.Equal("No Tasks tagged #work.", vm.EmptyMessage);
    }

    [Fact]
    public async Task A_search_without_matches_says_so_and_offers_to_clear_it()
    {
        var (service, _, vm) = Start();
        await service.CreateTaskAsync("Buy milk", Ct);
        await vm.LoadAsync(Ct);

        vm.SearchText = "xyz";
        await vm.ApplySearchCommand.ExecuteAsync(null);

        Assert.Equal("No Tasks match 'xyz'", vm.EmptyMessage);
        Assert.True(vm.ShowClearSearch);
        Assert.Empty(vm.Tasks);
    }

    [Fact]
    public async Task Clear_search_restores_the_view_like_Esc_does()
    {
        var (service, _, vm) = Start();
        await service.CreateTaskAsync("Buy milk", Ct);
        await vm.LoadAsync(Ct);
        vm.SearchText = "xyz";
        await vm.ApplySearchCommand.ExecuteAsync(null);

        await vm.ClearSearchCommand.ExecuteAsync(null);

        Assert.Equal("", vm.SearchText);
        Assert.Equal(["Buy milk"], vm.Tasks.Select(t => t.Title));
        Assert.Equal("", vm.EmptyMessage);
        Assert.False(vm.ShowClearSearch);
    }

    [Fact]
    public async Task Filters_without_matches_get_their_own_message_and_no_clear_search_button()
    {
        var (service, _, vm) = Start();
        await service.CreateTaskAsync("Buy milk", Ct);
        vm.HasDueFilter = true;

        await vm.LoadAsync(Ct);

        Assert.True(vm.IsPageEmpty);
        Assert.Equal("No Tasks match the filters.", vm.EmptyMessage);
        Assert.False(vm.ShowClearSearch);
    }

    [Fact]
    public async Task Adding_the_first_Task_hides_the_empty_message()
    {
        var (_, _, vm) = Start();
        await vm.LoadAsync(Ct);
        Assert.True(vm.HasEmptyMessage);

        vm.QuickAddTitle = "First";
        await vm.AddTaskCommand.ExecuteAsync(null);

        Assert.False(vm.HasEmptyMessage);
        Assert.Equal("", vm.EmptyMessage);
    }

    [Fact]
    public async Task A_List_with_only_completed_Tasks_is_not_empty()
    {
        var (service, _, vm) = Start();
        var task = await service.CreateTaskAsync("Done", Ct);
        await service.SetCompletedAsync(task.Id, true, Ct);

        await vm.LoadAsync(Ct);

        Assert.False(vm.IsPageEmpty);
        Assert.Equal("", vm.EmptyMessage);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(SmartView.Today, true)]
    [InlineData(SmartView.Overdue, true)]
    [InlineData(SmartView.Upcoming, true)]
    [InlineData(SmartView.All, true)]
    [InlineData(SmartView.Completed, false)]
    public async Task Quick_add_is_shown_on_every_page_except_the_Completed_Smart_View(SmartView? view, bool shown)
    {
        var (_, _, vm) = Start();

        await vm.ShowSmartViewAsync(view, Ct);

        Assert.Equal(shown, vm.ShowsQuickAdd);
    }

    [Fact]
    public async Task Switching_to_and_from_the_Completed_Smart_View_notifies_that_quick_add_changed()
    {
        var (_, _, vm) = Start();
        var changed = 0;
        vm.PropertyChanged += (_, e) => changed += e.PropertyName == nameof(MainViewModel.ShowsQuickAdd) ? 1 : 0;

        await vm.ShowSmartViewAsync(SmartView.Completed, Ct);
        await vm.ShowSmartViewAsync(null, Ct);

        Assert.Equal(2, changed);
    }

    [Fact]
    public async Task Quick_add_is_shown_on_a_Tag_page()
    {
        var (_, tags, vm) = Start();
        await vm.ShowSmartViewAsync(SmartView.Completed, Ct);
        var tag = await tags.CreateTagAsync("work", Ct);

        await vm.FilterByTagAsync(tag, Ct);

        Assert.True(vm.ShowsQuickAdd);
    }

    [Fact]
    public async Task Focusing_quick_add_on_the_Completed_Smart_View_shows_the_Default_List_then_asks_the_view()
    {
        var (_, _, vm) = Start();
        await vm.ShowSmartViewAsync(SmartView.Completed, Ct);
        SmartView? viewWhenAsked = SmartView.Completed;
        var asked = 0;
        vm.FocusQuickAddRequested += (_, _) =>
        {
            asked++;
            viewWhenAsked = vm.CurrentView;
        };

        await vm.FocusQuickAddCommand.ExecuteAsync(null);

        Assert.Null(vm.CurrentView);
        Assert.True(vm.ShowsQuickAdd);
        Assert.Equal(1, asked);
        Assert.Null(viewWhenAsked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(SmartView.Today)]
    [InlineData(SmartView.All)]
    public async Task Focusing_quick_add_elsewhere_only_asks_the_view_and_keeps_the_page(SmartView? view)
    {
        var (_, _, vm) = Start();
        await vm.ShowSmartViewAsync(view, Ct);
        var asked = 0;
        vm.FocusQuickAddRequested += (_, _) => asked++;

        await vm.FocusQuickAddCommand.ExecuteAsync(null);

        Assert.Equal(view, vm.CurrentView);
        Assert.Equal(1, asked);
    }
}
