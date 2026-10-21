using System.Collections.Specialized;
using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

/// <summary>
/// A Task's Subtasks nested under its row in the one Task list: the chevron's open state, the Subtask rows each
/// group shows, and how they follow page loads, searches, Filters, completion and the detail pane's edits.
/// </summary>
public class NestedSubtasksViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // "trip" with the given Subtasks, then "other" with none.
    private static async Task<(FakeTaskService Service, MainViewModel Vm)> StartAsync(
        params string[] subtasks)
    {
        var service = new FakeTaskService();
        var trip = await service.CreateTaskAsync("trip", Ct);
        await service.CreateTaskAsync("other", Ct);
        foreach (var title in subtasks)
        {
            await service.AddSubtaskAsync(trip.Id, title, Ct);
        }

        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());
        await vm.LoadAsync(Ct);
        return (service, vm);
    }

    // A group's rows as the owner reads them: a Task by its title, a Subtask row as "- " and its title.
    private static List<string> Rows(MainViewModel vm, TaskGroupKind kind) =>
        vm.Groups.SingleOrDefault(g => g.Kind == kind) is { } group
            ? [.. group.Select(r => r is SubtaskListRowViewModel ? "- " + r.Title : r.Title)]
            : [];

    private static TaskRowViewModel RowOf(MainViewModel vm, string title) =>
        vm.Tasks.Concat(vm.OverdueTasks).Concat(vm.CompletedTasks).Single(r => r.Title == title);

    [Fact]
    public async Task Every_Task_starts_closed_and_opening_one_lists_its_Subtasks_under_it_in_manual_order()
    {
        var (_, vm) = await StartAsync("book hotel", "pack", "buy tickets");
        var trip = RowOf(vm, "trip");

        Assert.False(trip.IsOpen);
        Assert.Equal(["trip", "other"], Rows(vm, TaskGroupKind.Open));

        vm.OpenTask(trip);

        Assert.True(trip.IsOpen);
        Assert.Equal(["trip", "- book hotel", "- pack", "- buy tickets", "other"], Rows(vm, TaskGroupKind.Open));
        Assert.All(vm.Groups.Single().OfType<SubtaskListRowViewModel>(), s => Assert.Same(trip, s.TaskRow));

        vm.CloseTask(trip);

        Assert.False(trip.IsOpen);
        Assert.Equal(["trip", "other"], Rows(vm, TaskGroupKind.Open));
    }

    [Fact]
    public async Task The_chevron_command_opens_a_closed_Task_and_closes_an_open_one_and_ignores_a_Task_without_Subtasks()
    {
        var (_, vm) = await StartAsync("pack");

        vm.ToggleTaskOpenCommand.Execute(RowOf(vm, "trip"));
        Assert.Equal(["trip", "- pack", "other"], Rows(vm, TaskGroupKind.Open));
        vm.ToggleTaskOpenCommand.Execute(RowOf(vm, "trip"));
        Assert.Equal(["trip", "other"], Rows(vm, TaskGroupKind.Open));

        vm.ToggleTaskOpenCommand.Execute(RowOf(vm, "other"));
        vm.OpenTask(RowOf(vm, "other"));
        Assert.False(RowOf(vm, "other").IsOpen);
        Assert.Equal(["trip", "other"], Rows(vm, TaskGroupKind.Open));
    }

    [Fact]
    public async Task Opening_and_closing_inserts_and_removes_only_that_Tasks_Subtask_rows_and_keeps_the_selection()
    {
        var (_, vm) = await StartAsync("book hotel", "pack");
        var other = RowOf(vm, "other");
        vm.SelectedTask = other;
        var group = vm.Groups.Single();
        var kept = group.ToList();
        var changes = new List<(NotifyCollectionChangedAction Action, int Index)>();
        group.CollectionChanged += (_, e) => changes.Add(
            (e.Action, e.Action == NotifyCollectionChangedAction.Remove ? e.OldStartingIndex : e.NewStartingIndex));

        vm.OpenTask(RowOf(vm, "trip"));
        vm.CloseTask(RowOf(vm, "trip"));

        Assert.Equal(
            [
                (NotifyCollectionChangedAction.Add, 1), (NotifyCollectionChangedAction.Add, 2),
                (NotifyCollectionChangedAction.Remove, 2), (NotifyCollectionChangedAction.Remove, 1),
            ],
            changes);
        Assert.Equal(kept, group.ToList());
        Assert.Same(other, vm.SelectedTask);
    }

    [Fact]
    public async Task A_Task_stays_open_across_page_loads_searches_and_Filter_changes_and_nothing_is_saved()
    {
        var service = new FakeTaskService();
        var trip = await service.CreateTaskAsync("trip", Ct);
        await service.AddSubtaskAsync(trip.Id, "pack", Ct);
        var vm = new MainViewModel(
            service, new FakeTagService(service), new FakeTimeProvider(), new AllViewDueService(service),
            new TitleSearchService(service));
        await vm.LoadAsync(Ct);
        vm.OpenTask(vm.Tasks.Single());

        await vm.ShowSmartViewAsync(SmartView.All, Ct);
        Assert.Equal(["trip", "- pack"], Rows(vm, TaskGroupKind.Open));
        await vm.ShowSmartViewAsync(null, Ct);
        Assert.Equal(["trip", "- pack"], Rows(vm, TaskGroupKind.Open));

        vm.SearchText = "trip";
        await vm.ApplySearchCommand.ExecuteAsync(null);
        Assert.Equal(["trip", "- pack"], Rows(vm, TaskGroupKind.Open));
        await vm.ClearSearchCommand.ExecuteAsync(null);
        Assert.Equal(["trip", "- pack"], Rows(vm, TaskGroupKind.Open));

        await vm.ToggleHasDueFilterCommand.ExecuteAsync(null);
        Assert.Empty(Rows(vm, TaskGroupKind.Open));
        await vm.ToggleHasDueFilterCommand.ExecuteAsync(null);
        Assert.Equal(["trip", "- pack"], Rows(vm, TaskGroupKind.Open));

        // Closed here, it is closed after the next load too.
        vm.CloseTask(vm.Tasks.Single());
        await vm.LoadAsync(Ct);
        Assert.Equal(["trip"], Rows(vm, TaskGroupKind.Open));

        // Opening and closing wrote nothing.
        Assert.Empty(service.Updates);
        Assert.Empty(service.Moves);
        Assert.Empty(service.Completions);
    }

    [Fact]
    public async Task Overdue_N_Completed_N_a_Tag_page_and_the_Completed_Smart_View_show_the_same_rows_and_count_Tasks_only()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        var service = new FakeTaskService();
        var late = await service.CreateTaskAsync("late", Ct);
        var done = await service.CreateTaskAsync("done", Ct);
        await service.AddSubtaskAsync(late.Id, "late step", Ct);
        await service.AddSubtaskAsync(done.Id, "done step", Ct);
        service.Put(FakeTaskService.With(service.All[0], t => t.DueDate = new DateOnly(2026, 10, 18)));
        await service.SetCompletedAsync(done.Id, true, Ct);
        var tags = new FakeTagService(service);
        var tag = await tags.AddTagToTaskAsync(late.Id, "work", Ct);
        var vm = new MainViewModel(service, tags, clock, new AllViewDueService(service, clock));
        await vm.LoadAsync(Ct);
        vm.OpenTask(RowOf(vm, "late"));
        vm.OpenTask(RowOf(vm, "done"));
        vm.IsCompletedExpanded = true;

        Assert.Equal(["late", "- late step"], Rows(vm, TaskGroupKind.Open));
        Assert.Equal(["done", "- done step"], Rows(vm, TaskGroupKind.Completed));
        Assert.Equal("Completed (1)", vm.Groups.Single(g => g.Kind == TaskGroupKind.Completed).Heading);
        Assert.Equal("Completed (1)", vm.CompletedHeader);

        await vm.ShowSmartViewAsync(SmartView.Today, Ct);
        Assert.Equal(["late", "- late step"], Rows(vm, TaskGroupKind.Overdue));
        Assert.Equal("Overdue (1)", vm.Groups.Single(g => g.Kind == TaskGroupKind.Overdue).Heading);
        Assert.Equal("Overdue (1)", vm.OverdueHeader);

        await vm.ShowAsync(new TaskListPage.TagPage(tag), Ct);
        Assert.Equal(["late", "- late step"], Rows(vm, TaskGroupKind.Open));

        await vm.ShowSmartViewAsync(SmartView.Completed, Ct);
        Assert.Equal(["done", "- done step"], Rows(vm, TaskGroupKind.Open));
    }

    [Fact]
    public async Task A_ticked_Subtask_is_dimmed_and_struck_through_and_a_completed_Tasks_block_dims_except_on_the_Completed_Smart_View()
    {
        var clock = new FakeTimeProvider();
        var service = new FakeTaskService();
        var trip = await service.CreateTaskAsync("trip", Ct);
        var ticked = await service.AddSubtaskAsync(trip.Id, "ticked", Ct);
        await service.AddSubtaskAsync(trip.Id, "left", Ct);
        await service.SetSubtaskCompletedAsync(ticked.Id, true, Ct);
        var vm = new MainViewModel(service, new FakeTagService(service), clock, new AllViewDueService(service, clock));
        await vm.LoadAsync(Ct);
        var row = vm.Tasks.Single();
        var (tickedRow, leftRow) = (row.SubtaskRows[0], row.SubtaskRows[1]);

        Assert.Equal((true, true, true), (tickedRow.IsCompleted, tickedRow.IsDimmed, tickedRow.IsStruckThrough));
        Assert.Equal((false, false, false), (leftRow.IsCompleted, leftRow.IsDimmed, leftRow.IsStruckThrough));

        await vm.ToggleCompleteCommand.ExecuteAsync(row);
        Assert.True(leftRow.IsDimmed, "A Subtask row under a completed Task is not at the Task's 60 %.");
        Assert.False(leftRow.IsStruckThrough);
        Assert.True(tickedRow.IsStruckThrough);

        await vm.ShowSmartViewAsync(SmartView.Completed, Ct);
        var full = vm.Tasks.Single().SubtaskRows;
        Assert.All(full, s => Assert.False(s.IsDimmed || s.IsStruckThrough));
        Assert.True(full[0].IsCompleted);
    }

    [Fact]
    public async Task Completing_an_open_Task_holds_its_block_then_moves_it_into_Completed_N_and_undo_brings_it_back_whole()
    {
        var clock = new FakeTimeProvider();
        var service = new FakeTaskService();
        var trip = await service.CreateTaskAsync("trip", Ct);
        await service.CreateTaskAsync("other", Ct);
        await service.AddSubtaskAsync(trip.Id, "pack", Ct);
        await service.AddSubtaskAsync(trip.Id, "go", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service), clock)
        {
            CompletionMoveDelay = () => TimeSpan.FromMilliseconds(400),
            IsCompletedExpanded = true,
        };
        await vm.LoadAsync(Ct);
        var row = RowOf(vm, "trip");
        vm.OpenTask(row);

        await vm.ToggleCompleteCommand.ExecuteAsync(row);
        Assert.Equal(["trip", "- pack", "- go", "other"], Rows(vm, TaskGroupKind.Open));

        clock.Advance(TimeSpan.FromMilliseconds(400));
        await vm.CompletionMove;
        Assert.Equal(["other"], Rows(vm, TaskGroupKind.Open));
        Assert.Equal(["trip", "- pack", "- go"], Rows(vm, TaskGroupKind.Completed));

        // Ctrl+Z on the completion brings the block back whole.
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(["trip", "- pack", "- go", "other"], Rows(vm, TaskGroupKind.Open));
        Assert.Empty(Rows(vm, TaskGroupKind.Completed));

        // So does un-completing it in "Completed (N)".
        await vm.ToggleCompleteCommand.ExecuteAsync(row);
        clock.Advance(TimeSpan.FromMilliseconds(400));
        await vm.CompletionMove;
        await vm.ToggleCompleteCommand.ExecuteAsync(row);
        Assert.Equal(["trip", "- pack", "- go", "other"], Rows(vm, TaskGroupKind.Open));
        Assert.Empty(Rows(vm, TaskGroupKind.Completed));
    }

    [Fact]
    public async Task A_new_Occurrence_of_an_open_recurring_Task_starts_closed()
    {
        var (service, vm) = await StartAsync("pack");
        service.Put(FakeTaskService.With(service.All[0], t => t.Recurrence = Recurrence.Daily()));
        await vm.LoadAsync(Ct);
        vm.OpenTask(RowOf(vm, "trip"));

        await vm.ToggleCompleteCommand.ExecuteAsync(RowOf(vm, "trip"));

        var next = vm.Tasks.Single(r => r.Title == "trip");
        Assert.True(next.HasSubtasks);
        Assert.False(next.IsOpen);
        Assert.Equal(["trip", "other"], Rows(vm, TaskGroupKind.Open));
    }

    [Fact]
    public async Task A_Tasks_first_Subtask_shows_a_closed_chevron_and_deleting_its_last_drops_it_from_the_open_state()
    {
        var (_, vm) = await StartAsync();
        var trip = RowOf(vm, "trip");
        vm.SelectedTask = trip;
        Assert.False(trip.HasSubtasks);

        vm.NewSubtaskTitle = "pack";
        await vm.AddSubtaskCommand.ExecuteAsync(null);
        Assert.True(trip.HasSubtasks);
        Assert.False(trip.IsOpen);
        Assert.Equal(["trip", "other"], Rows(vm, TaskGroupKind.Open));

        vm.OpenTask(trip);
        await vm.DeleteSubtaskCommand.ExecuteAsync(vm.SubtaskItems.Single());
        Assert.False(trip.HasSubtasks);
        Assert.False(trip.IsOpen);
        Assert.Equal(["trip", "other"], Rows(vm, TaskGroupKind.Open));

        // Its next first Subtask shows closed again: the Task left the open state with its last one.
        vm.NewSubtaskTitle = "go";
        await vm.AddSubtaskCommand.ExecuteAsync(null);
        Assert.False(trip.IsOpen);
        Assert.Equal(["trip", "other"], Rows(vm, TaskGroupKind.Open));
    }

    [Fact]
    public async Task Adding_deleting_renaming_reordering_and_ticking_in_the_pane_updates_the_open_Tasks_rows()
    {
        var (_, vm) = await StartAsync("book hotel", "pack");
        var trip = RowOf(vm, "trip");
        vm.SelectedTask = trip;
        vm.OpenTask(trip);
        var hotel = trip.SubtaskRows[0];

        vm.NewSubtaskTitle = "buy tickets";
        await vm.AddSubtaskCommand.ExecuteAsync(null);
        Assert.Equal(["trip", "- book hotel", "- pack", "- buy tickets", "other"], Rows(vm, TaskGroupKind.Open));

        vm.SubtaskItems[0].Title = "book the hotel";
        await vm.PendingSave;
        vm.SubtaskItems[0].IsCompleted = true;
        await vm.PendingSave;
        Assert.Equal(["trip", "- book the hotel", "- pack", "- buy tickets", "other"], Rows(vm, TaskGroupKind.Open));
        Assert.Same(hotel, trip.SubtaskRows[0]);
        Assert.True(hotel.IsCompleted);

        await vm.MoveSubtaskDownCommand.ExecuteAsync(vm.SubtaskItems[0]);
        Assert.Equal(["trip", "- pack", "- book the hotel", "- buy tickets", "other"], Rows(vm, TaskGroupKind.Open));

        await vm.DeleteSubtaskCommand.ExecuteAsync(vm.SubtaskItems[2]);
        Assert.Equal(["trip", "- pack", "- book the hotel", "other"], Rows(vm, TaskGroupKind.Open));

        // Undo puts the deleted Subtask's row back in its place.
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(["trip", "- pack", "- book the hotel", "- buy tickets", "other"], Rows(vm, TaskGroupKind.Open));
    }

    // Answers the Smart Views from the fake Task service, judged by the clock.
    private sealed class AllViewDueService(FakeTaskService tasks, TimeProvider? clock = null) : IDueService
    {
        public Task<TaskItem> SetDueAsync(
            Guid taskId, DateOnly? dueDate, TimeOnly? dueTime, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SmartViewCounts> CountSmartViewsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TaskItem>> ListSmartViewAsync(
            SmartView view, int skip = 0, CancellationToken cancellationToken = default)
        {
            var now = (clock ?? TimeProvider.System).GetUtcNow();
            var zone = (clock ?? TimeProvider.System).LocalTimeZone;
            IEnumerable<TaskItem> result = view switch
            {
                SmartView.Today => tasks.All.Where(t => DueCalculator.IsInToday(t.DueDate, t.IsCompleted, now, zone)
                    || DueCalculator.IsOverdue(t.DueDate, t.DueTime, t.IsCompleted, now, zone)),
                SmartView.Completed => tasks.All.Where(t => t.IsCompleted),
                _ => tasks.All.Where(t => !t.IsCompleted),
            };
            return Task.FromResult<IReadOnlyList<TaskItem>>([.. result]);
        }
    }

    // Matches the title and the Has Due Filter, nothing else.
    private sealed class TitleSearchService(FakeTaskService tasks) : ITaskSearchService
    {
        public Task<IReadOnlyList<TaskItem>> SearchAsync(
            TaskSearchQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>(
            [
                .. tasks.All.Where(t =>
                    (string.IsNullOrWhiteSpace(query.Text)
                        || t.Title.Contains(query.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                    && (!query.HasDue || t.DueDate != null)),
            ]);
    }
}
