using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

public class SearchViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Matches on the title only; the real matching rules are covered by the Data tests.
    private sealed class FakeSearchService(FakeTaskService tasks) : ITaskSearchService
    {
        public List<TaskSearchQuery> Queries { get; } = [];

        public Task<IReadOnlyList<TaskItem>> SearchAsync(
            TaskSearchQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            IEnumerable<TaskItem> result = tasks.All.Where(t =>
                (string.IsNullOrWhiteSpace(query.Text)
                    || t.Title.Contains(query.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                && (query.MinPriority is not { } min || t.Priority >= min)
                && (query.TagIds ?? []).All(id => t.Tags.Any(g => g.Id == id))
                && (!query.HasDue || t.DueDate != null));
            return Task.FromResult<IReadOnlyList<TaskItem>>([.. result]);
        }
    }

    private static async Task<(FakeTaskService Service, FakeTagService Tags, FakeSearchService Search, MainViewModel Vm)>
        StartAsync(params string[] titles)
    {
        var service = new FakeTaskService();
        foreach (var title in titles)
        {
            await service.CreateTaskAsync(title, Ct);
        }

        var tags = new FakeTagService(service);
        var search = new FakeSearchService(service);
        var vm = new MainViewModel(service, tags, searchService: search);
        await vm.LoadAsync(Ct);
        return (service, tags, search, vm);
    }

    [Fact]
    public async Task Without_search_or_filters_the_search_service_is_not_used()
    {
        var (_, _, search, vm) = await StartAsync("a", "b");

        Assert.False(vm.IsNarrowed);
        Assert.Empty(search.Queries);
        Assert.Equal(["a", "b"], vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Applying_a_search_narrows_the_view_and_turns_reordering_off()
    {
        var (_, _, search, vm) = await StartAsync("Buy milk", "Walk dog");

        vm.SearchText = "MILK";
        Assert.False(vm.IsReorderEnabled);
        await vm.ApplySearchCommand.ExecuteAsync(null);

        Assert.Equal(["Buy milk"], vm.Tasks.Select(t => t.Title));
        Assert.Equal("MILK", search.Queries.Last().Text);
    }

    [Fact]
    public async Task Clearing_the_search_restores_the_view_and_reordering()
    {
        var (_, _, _, vm) = await StartAsync("Buy milk", "Walk dog");
        vm.SearchText = "milk";
        await vm.ApplySearchCommand.ExecuteAsync(null);

        await vm.ClearSearchCommand.ExecuteAsync(null);

        Assert.Equal("", vm.SearchText);
        Assert.Equal(["Buy milk", "Walk dog"], vm.Tasks.Select(t => t.Title));
        Assert.True(vm.IsReorderEnabled);
    }

    [Fact]
    public async Task Moving_a_task_does_nothing_while_searching()
    {
        var (service, _, _, vm) = await StartAsync("milk a", "milk b", "other");
        vm.SearchText = "milk";
        await vm.ApplySearchCommand.ExecuteAsync(null);

        await vm.MoveTaskAsync(0, 1, Ct);

        Assert.Empty(service.Moves);
        Assert.Equal(["milk a", "milk b"], vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Priority_filter_applies_and_none_means_any()
    {
        var (service, _, _, vm) = await StartAsync("low", "high");
        await service.UpdateTaskAsync(service.All[0].Id, "low", null, Priority.Low, Ct);
        await service.UpdateTaskAsync(service.All[1].Id, "high", null, Priority.High, Ct);

        await vm.SetMinPriorityFilterAsync(Priority.Medium, Ct);
        Assert.Equal(["high"], vm.Tasks.Select(t => t.Title));
        Assert.False(vm.IsReorderEnabled);

        await vm.SetMinPriorityFilterAsync(Priority.None, Ct);
        Assert.Equal(["low", "high"], vm.Tasks.Select(t => t.Title));
        Assert.True(vm.IsReorderEnabled);
    }

    [Fact]
    public async Task Tag_chips_require_all_selected_tags_and_toggle_off()
    {
        var (service, tags, search, vm) = await StartAsync("both", "work only", "none");
        await tags.AddTagToTaskAsync(service.All[0].Id, "work", Ct);
        await tags.AddTagToTaskAsync(service.All[0].Id, "home", Ct);
        await tags.AddTagToTaskAsync(service.All[1].Id, "work", Ct);
        await vm.LoadAsync(Ct);
        var work = vm.Tags.Single(t => t.Name == "work");
        var home = vm.Tags.Single(t => t.Name == "home");

        await vm.ToggleFilterTagCommand.ExecuteAsync(work);
        Assert.Equal(["both", "work only"], vm.Tasks.Select(t => t.Title));

        await vm.ToggleFilterTagCommand.ExecuteAsync(home);
        Assert.Equal(["both"], vm.Tasks.Select(t => t.Title));
        Assert.Equal(2, search.Queries.Last().TagIds!.Count);
        Assert.Equal(2, vm.FilterTagChips.Count(c => c.IsSelected));

        await vm.ToggleFilterTagCommand.ExecuteAsync(home);
        await vm.ToggleFilterTagCommand.ExecuteAsync(work);
        Assert.False(vm.IsNarrowed);
        Assert.Equal(3, vm.Tasks.Count);
    }

    [Fact]
    public async Task Deleting_a_filtered_tag_drops_it_from_the_filter()
    {
        var (service, tags, _, vm) = await StartAsync("a");
        await tags.AddTagToTaskAsync(service.All[0].Id, "work", Ct);
        await vm.LoadAsync(Ct);
        await vm.ToggleFilterTagCommand.ExecuteAsync(vm.Tags.Single());

        await vm.DeleteTagAsync(vm.Tags.Single(), Ct);

        Assert.Empty(vm.FilterTags);
        Assert.False(vm.IsNarrowed);
        Assert.Equal(["a"], vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Has_due_filter_toggles()
    {
        var (service, _, _, vm) = await StartAsync("dated", "undated");
        service.All[0].DueDate = new DateOnly(2026, 10, 20);

        await vm.ToggleHasDueFilterCommand.ExecuteAsync(null);
        Assert.Equal(["dated"], vm.Tasks.Select(t => t.Title));

        await vm.ToggleHasDueFilterCommand.ExecuteAsync(null);
        Assert.Equal(["dated", "undated"], vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Search_is_scoped_to_the_tag_view_and_keeps_filters_when_switching()
    {
        var (service, tags, search, vm) = await StartAsync("milk tagged", "milk plain");
        var tag = await tags.AddTagToTaskAsync(service.All[0].Id, "t", Ct);
        await vm.LoadAsync(Ct);
        vm.SearchText = "milk";

        await vm.FilterByTagAsync(vm.Tags.Single(), Ct);

        Assert.Equal(tag.Id, search.Queries.Last().ViewTagId);
        Assert.Null(search.Queries.Last().View);
        Assert.False(vm.IsReorderEnabled);
    }

    [Fact]
    public async Task Clear_all_resets_text_and_every_chip()
    {
        var (_, _, _, vm) = await StartAsync("a");
        vm.SearchText = "x";
        await vm.SetMinPriorityFilterAsync(Priority.High, Ct);
        await vm.ToggleHasDueFilterCommand.ExecuteAsync(null);

        await vm.ClearAllFiltersCommand.ExecuteAsync(null);

        Assert.False(vm.IsNarrowed);
        Assert.Null(vm.MinPriorityFilter);
        Assert.False(vm.HasDueFilter);
        Assert.Equal(["a"], vm.Tasks.Select(t => t.Title));
        Assert.True(vm.IsReorderEnabled);
    }

    [Fact]
    public async Task A_task_added_while_searching_is_shown_only_if_it_matches()
    {
        var (_, _, _, vm) = await StartAsync("milk");
        vm.SearchText = "milk";
        await vm.ApplySearchCommand.ExecuteAsync(null);

        vm.QuickAddTitle = "bread";
        await vm.AddTaskCommand.ExecuteAsync(null);
        Assert.Equal(["milk"], vm.Tasks.Select(t => t.Title));

        vm.QuickAddTitle = "milk 2";
        await vm.AddTaskCommand.ExecuteAsync(null);
        Assert.Equal(["milk", "milk 2"], vm.Tasks.Select(t => t.Title));
    }
}
