using Microsoft.Extensions.Time.Testing;
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

    // Holds a search until released, then fails as a disposed database does (its window closed and let it go).
    private sealed class DisposedOnReleaseSearchService : ITaskSearchService
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.SetResult();

        public async Task<IReadOnlyList<TaskItem>> SearchAsync(
            TaskSearchQuery query, CancellationToken cancellationToken = default)
        {
            await _release.Task;
            throw new ObjectDisposedException("IServiceProvider");
        }
    }

    [Fact]
    public async Task A_search_the_stop_cancelled_while_loading_ends_quietly_when_its_database_is_then_gone()
    {
        var search = new DisposedOnReleaseSearchService();
        var service = new FakeTaskService();
        // A fake clock, so the search the typing queued never runs on its own.
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider(), searchService: search);
        vm.SearchText = "milk";

        var run = vm.ApplySearchCommand.ExecuteAsync(null);
        var stop = vm.StopAsync();
        search.Release();

        await run;
        await stop;
    }

    [Fact]
    public async Task A_search_whose_database_is_gone_still_fails_when_nothing_stopped_it()
    {
        var search = new DisposedOnReleaseSearchService();
        var service = new FakeTaskService();
        // A fake clock, so the search the typing queued never runs on its own.
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider(), searchService: search);
        vm.SearchText = "milk";

        var run = vm.ApplySearchCommand.ExecuteAsync(null);
        search.Release();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => run);
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
        var vm = new MainViewModel(service, tags, new FakeTimeProvider(), searchService: search);
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
    public async Task Clear_filters_resets_every_chip_and_keeps_the_search_text()
    {
        var (_, _, _, vm) = await StartAsync("a");
        vm.SearchText = "x";
        await vm.SetMinPriorityFilterAsync(Priority.High, Ct);
        await vm.ToggleHasDueFilterCommand.ExecuteAsync(null);

        await vm.ClearAllFiltersCommand.ExecuteAsync(null);

        Assert.Equal("x", vm.SearchText);
        Assert.True(vm.IsNarrowed);
        Assert.Null(vm.MinPriorityFilter);
        Assert.False(vm.HasDueFilter);
        Assert.False(vm.HasFilter);
        Assert.Empty(vm.Tasks);
        Assert.False(vm.IsReorderEnabled);
    }

    [Fact]
    public async Task Has_filter_is_false_with_only_search_text_and_true_with_any_single_filter()
    {
        var (service, tags, _, vm) = await StartAsync("a");
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SearchText = "a";
        Assert.False(vm.HasFilter);
        Assert.DoesNotContain(nameof(MainViewModel.HasFilter), changed);
        vm.SearchText = "";

        await vm.SetMinPriorityFilterAsync(Priority.High, Ct);
        Assert.True(vm.HasFilter);
        await vm.SetMinPriorityFilterAsync(null, Ct);
        Assert.False(vm.HasFilter);

        await vm.ToggleHasDueFilterCommand.ExecuteAsync(null);
        Assert.True(vm.HasFilter);
        await vm.ToggleHasDueFilterCommand.ExecuteAsync(null);
        Assert.False(vm.HasFilter);

        await vm.ToggleHasReminderFilterCommand.ExecuteAsync(null);
        Assert.True(vm.HasFilter);
        await vm.ToggleHasReminderFilterCommand.ExecuteAsync(null);
        Assert.False(vm.HasFilter);

        var tag = await tags.AddTagToTaskAsync(service.All[0].Id, "t", Ct);
        await vm.LoadAsync(Ct);
        await vm.ToggleFilterTagCommand.ExecuteAsync(tag);
        Assert.True(vm.HasFilter);
        await vm.ToggleFilterTagCommand.ExecuteAsync(tag);
        Assert.False(vm.HasFilter);
        Assert.Contains(nameof(MainViewModel.HasFilter), changed);
    }

    [Fact]
    public async Task Clear_filters_keeps_the_search_text_and_the_list_still_shows_its_results()
    {
        var (service, _, _, vm) = await StartAsync("milk", "milk due", "bread");
        service.All[1].DueDate = new DateOnly(2026, 1, 1);
        vm.SearchText = "milk";
        await vm.ToggleHasDueFilterCommand.ExecuteAsync(null);
        Assert.Equal(["milk due"], vm.Tasks.Select(t => t.Title));

        await vm.ClearAllFiltersCommand.ExecuteAsync(null);

        Assert.Equal("milk", vm.SearchText);
        Assert.False(vm.HasFilter);
        Assert.Equal(["milk", "milk due"], vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task The_filter_row_is_hidden_by_default()
    {
        var (_, _, _, vm) = await StartAsync("a");

        Assert.False(vm.IsFilterBarOpen);
        Assert.False(vm.IsFilterBarShown);
    }

    [Fact]
    public async Task Toggling_the_filter_bar_shows_the_row_and_toggling_again_hides_it()
    {
        var (_, _, _, vm) = await StartAsync("a");
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ToggleFilterBarCommand.Execute(null);
        Assert.True(vm.IsFilterBarShown);
        Assert.Contains(nameof(MainViewModel.IsFilterBarShown), changed);

        vm.ToggleFilterBarCommand.Execute(null);
        Assert.False(vm.IsFilterBarShown);
    }

    [Fact]
    public async Task Turning_on_a_filter_shows_the_row_and_closing_the_toggle_cannot_hide_it()
    {
        var (_, _, _, vm) = await StartAsync("a");
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.ToggleHasDueFilterCommand.ExecuteAsync(null);
        Assert.False(vm.IsFilterBarOpen);
        Assert.True(vm.IsFilterBarShown);
        Assert.Contains(nameof(MainViewModel.IsFilterBarShown), changed);

        vm.ToggleFilterBarCommand.Execute(null);
        vm.ToggleFilterBarCommand.Execute(null);
        Assert.False(vm.IsFilterBarOpen);
        Assert.True(vm.IsFilterBarShown);
    }

    [Fact]
    public async Task Search_text_alone_narrows_the_view_but_does_not_show_the_filter_row()
    {
        var (_, _, _, vm) = await StartAsync("a");

        vm.SearchText = "a";
        await vm.ApplySearchCommand.ExecuteAsync(null);

        Assert.True(vm.IsNarrowed);
        Assert.False(vm.HasFilter);
        Assert.False(vm.IsFilterBarShown);
    }

    [Fact]
    public async Task Clear_filters_hides_the_row_when_the_toggle_was_closed()
    {
        var (service, tags, _, vm) = await StartAsync("a");
        var tag = await tags.AddTagToTaskAsync(service.All[0].Id, "t", Ct);
        await vm.LoadAsync(Ct);
        vm.SearchText = "a";
        await vm.ToggleFilterTagCommand.ExecuteAsync(tag);
        Assert.True(vm.IsFilterBarShown);

        await vm.ClearAllFiltersCommand.ExecuteAsync(null);

        Assert.Equal("a", vm.SearchText);
        Assert.False(vm.HasFilter);
        Assert.False(vm.IsFilterBarShown);
    }

    [Fact]
    public async Task Clear_filters_keeps_the_row_shown_when_the_toggle_was_open()
    {
        var (_, _, _, vm) = await StartAsync("a");
        vm.ToggleFilterBarCommand.Execute(null);
        await vm.SetMinPriorityFilterAsync(Priority.High, Ct);

        await vm.ClearAllFiltersCommand.ExecuteAsync(null);

        Assert.False(vm.HasFilter);
        Assert.True(vm.IsFilterBarOpen);
        Assert.True(vm.IsFilterBarShown);
    }

    [Fact]
    public async Task The_filter_row_keeps_its_state_across_view_switches()
    {
        var (service, tags, _, vm) = await StartAsync("a");
        await tags.AddTagToTaskAsync(service.All[0].Id, "t", Ct);
        await vm.LoadAsync(Ct);
        vm.ToggleFilterBarCommand.Execute(null);

        await vm.FilterByTagAsync(vm.Tags.Single(), Ct);
        Assert.True(vm.IsFilterBarShown);

        await vm.FilterByTagAsync(null, Ct);
        Assert.True(vm.IsFilterBarShown);
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

    [Fact]
    public async Task Clicking_the_toggle_while_a_filter_is_on_does_not_open_the_row_and_repushes_it()
    {
        var (_, _, _, vm) = await StartAsync("a");
        await vm.ToggleHasDueFilterCommand.ExecuteAsync(null);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ToggleFilterBarCommand.Execute(null);

        Assert.False(vm.IsFilterBarOpen);
        Assert.Contains(nameof(MainViewModel.IsFilterBarShown), changed);

        await vm.ClearAllFiltersCommand.ExecuteAsync(null);

        Assert.False(vm.IsFilterBarShown);
    }

    [Fact]
    public async Task Clearing_a_tag_only_filter_turns_reordering_back_on()
    {
        var (service, tags, _, vm) = await StartAsync("a");
        var tag = await tags.AddTagToTaskAsync(service.All[0].Id, "t", Ct);
        await vm.LoadAsync(Ct);
        await vm.ToggleFilterTagCommand.ExecuteAsync(tag);
        Assert.False(vm.IsReorderEnabled);

        await vm.ClearAllFiltersCommand.ExecuteAsync(null);

        Assert.True(vm.IsReorderEnabled);
    }
}
