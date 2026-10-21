using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

/// <summary>
/// While a search is on, a Task matching through a Subtask title shows open with its whole checklist; when the
/// search ends every Task the search opened goes back to how it was.
/// </summary>
public class SubtaskSearchOpenViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // "trip" (Subtasks "Ângela's gift", "pack"), "ângela" with none, "plain" (Subtask "socks").
    private static async Task<MainViewModel> StartAsync()
    {
        var service = new FakeTaskService();
        var trip = await service.CreateTaskAsync("trip", Ct);
        await service.AddSubtaskAsync(trip.Id, "Ângela's gift", Ct);
        await service.AddSubtaskAsync(trip.Id, "pack", Ct);
        await service.CreateTaskAsync("ângela", Ct);
        var plain = await service.CreateTaskAsync("plain", Ct);
        await service.AddSubtaskAsync(plain.Id, "socks", Ct);
        var vm = new MainViewModel(
            service, new FakeTagService(service), new FakeTimeProvider(),
            searchService: new FoldingSearchService(service));
        await vm.LoadAsync(Ct);
        return vm;
    }

    private static List<string> Rows(MainViewModel vm) =>
        [.. vm.Groups.Single().Select(r => r is SubtaskListRowViewModel ? "- " + r.Title : r.Title)];

    private static TaskRowViewModel RowOf(MainViewModel vm, string title) => vm.Tasks.Single(r => r.Title == title);

    private static async Task SearchAsync(MainViewModel vm, string text)
    {
        vm.SearchText = text;
        await vm.ApplySearchCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task A_Task_matching_through_a_Subtask_title_opens_with_all_its_Subtasks_ignoring_case_by_the_search_rule()
    {
        var vm = await StartAsync();

        await SearchAsync(vm, "ÂNGELA");

        Assert.Equal(["trip", "- Ângela's gift", "- pack", "ângela"], Rows(vm));
        Assert.True(RowOf(vm, "trip").IsOpen);
    }

    [Fact]
    public async Task A_Task_matching_only_by_title_keeps_its_state()
    {
        var vm = await StartAsync();
        vm.OpenTask(RowOf(vm, "plain"));

        await SearchAsync(vm, "ângela");

        Assert.False(RowOf(vm, "ângela").IsOpen);
        Assert.Equal(["trip", "- Ângela's gift", "- pack", "ângela"], Rows(vm));

        await SearchAsync(vm, "plain");
        Assert.Equal(["plain", "- socks"], Rows(vm)); // kept open, not by the search
        await SearchAsync(vm, "");
        Assert.Equal(["trip", "ângela", "plain", "- socks"], Rows(vm));
    }

    [Fact]
    public async Task The_chevron_works_during_a_search()
    {
        var vm = await StartAsync();
        await SearchAsync(vm, "ângela");

        vm.ToggleTaskOpenCommand.Execute(RowOf(vm, "trip"));
        Assert.Equal(["trip", "ângela"], Rows(vm));
        vm.ToggleTaskOpenCommand.Execute(RowOf(vm, "trip"));
        Assert.Equal(["trip", "- Ângela's gift", "- pack", "ângela"], Rows(vm));
    }

    [Fact]
    public async Task Every_way_of_ending_the_search_closes_the_Tasks_the_search_opened()
    {
        var vm = await StartAsync();

        // "Clear search" (Esc): the rows shown close at once, and after the reload too.
        await SearchAsync(vm, "ângela");
        await vm.ClearSearchCommand.ExecuteAsync(null);
        Assert.Equal(["trip", "ângela", "plain"], Rows(vm));

        // Opening the List again.
        await SearchAsync(vm, "ângela");
        Assert.True(RowOf(vm, "trip").IsOpen);
        await vm.ShowListAsync(Guid.NewGuid(), Ct);
        Assert.Equal("", vm.SearchText);
        await vm.LoadAsync(Ct);
        Assert.All(vm.Tasks, r => Assert.False(r.IsOpen));

        // The text emptied by hand: the row already shown closes without a reload.
        await SearchAsync(vm, "ângela");
        var trip = RowOf(vm, "trip");
        Assert.True(trip.IsOpen);
        vm.SearchText = "";
        Assert.False(trip.IsOpen);
    }

    [Fact]
    public async Task A_Task_the_owner_closed_during_the_search_stays_closed_and_one_open_before_stays_open_after()
    {
        var vm = await StartAsync();
        vm.OpenTask(RowOf(vm, "plain"));
        await SearchAsync(vm, "s"); // trip: "Ângela's gift"; plain: "socks"
        Assert.Equal(["trip", "- Ângela's gift", "- pack", "plain", "- socks"], Rows(vm));

        vm.CloseTask(RowOf(vm, "trip"));
        await SearchAsync(vm, "so");
        Assert.Equal(["plain", "- socks"], Rows(vm));
        await SearchAsync(vm, "s");
        Assert.Equal(["trip", "plain", "- socks"], Rows(vm)); // a later search does not reopen it

        await vm.ClearSearchCommand.ExecuteAsync(null);
        Assert.False(RowOf(vm, "trip").IsOpen);
        Assert.True(RowOf(vm, "plain").IsOpen);
    }

    [Fact]
    public async Task A_Task_that_stops_matching_as_the_search_changes_goes_back_to_closed()
    {
        var vm = await StartAsync();

        await SearchAsync(vm, "ângela");
        await SearchAsync(vm, "trip");

        Assert.Equal(["trip"], Rows(vm));
        Assert.False(RowOf(vm, "trip").IsOpen);
    }

    // Matches the folded text in a Task's title or any Subtask's title, as the database search does.
    private sealed class FoldingSearchService(FakeTaskService tasks) : ITaskSearchService
    {
        public Task<IReadOnlyList<TaskItem>> SearchAsync(
            TaskSearchQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>(
            [
                .. tasks.All.Where(t =>
                    string.IsNullOrWhiteSpace(query.Text)
                    || SearchFold.Contains(t.Title, query.Text.Trim())
                    || t.Subtasks.Exists(s => SearchFold.Contains(s.Title, query.Text.Trim()))),
            ]);
    }
}
