using Microsoft.Extensions.Time.Testing;
using TaskDesk.Presentation;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

/// <summary>
/// The one selection of the Task list over Subtask rows: a selected Subtask row stands for its Task, Delete and
/// Space do nothing on it, closing its Task selects the Task, and it follows its block between groups.
/// </summary>
public class SubtaskRowSelectionViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // "trip" with the Subtasks "pack" and "go", open, then "other" with none.
    private static async Task<(FakeTaskService Service, MainViewModel Vm, TaskRowViewModel Trip)> StartAsync()
    {
        var service = new FakeTaskService();
        var trip = await service.CreateTaskAsync("trip", Ct);
        await service.CreateTaskAsync("other", Ct);
        await service.AddSubtaskAsync(trip.Id, "pack", Ct);
        await service.AddSubtaskAsync(trip.Id, "go", Ct);

        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());
        await vm.LoadAsync(Ct);
        var row = RowOf(vm, "trip");
        vm.OpenTask(row);
        return (service, vm, row);
    }

    private static TaskRowViewModel RowOf(MainViewModel vm, string title) =>
        vm.Tasks.Concat(vm.OverdueTasks).Concat(vm.CompletedTasks).Single(r => r.Title == title);

    [Fact]
    public async Task A_selected_Subtask_row_stands_for_its_Task_and_the_docked_pane_follows_it_to_the_Task()
    {
        var (_, vm, trip) = await StartAsync();
        var other = RowOf(vm, "other");
        vm.SelectedTask = other;
        var go = trip.SubtaskRows[1];

        vm.SelectedRow = go;

        Assert.Same(go, vm.SelectedRow);
        Assert.Same(go, vm.SelectedSubtaskRow);
        Assert.Same(trip, vm.SelectedTask);
        Assert.Same(trip, vm.DetailTask);

        // Another Subtask row of the same Task, then the Task's own row.
        vm.SelectedRow = trip.SubtaskRows[0];
        Assert.Same(trip.SubtaskRows[0], vm.SelectedSubtaskRow);
        Assert.Same(trip, vm.SelectedTask);
        vm.SelectedRow = trip;
        Assert.Null(vm.SelectedSubtaskRow);
        Assert.Same(trip, vm.SelectedRow);

        // Selecting another Task, also through SelectedTask, leaves the Subtask row.
        vm.SelectedRow = go;
        vm.SelectedTask = other;
        Assert.Null(vm.SelectedSubtaskRow);
        Assert.Same(other, vm.SelectedRow);
        Assert.Same(other, vm.DetailTask);
    }

    [Fact]
    public async Task Opening_the_detail_pane_from_a_selected_Subtask_row_opens_its_Task_and_keeps_the_row_selected()
    {
        var (_, vm, trip) = await StartAsync();
        vm.IsDetailPaneOpen = false;
        var go = trip.SubtaskRows[1];
        vm.SelectedRow = go;

        vm.OpenDetailPane();

        Assert.True(vm.IsDetailPaneOpen);
        Assert.Same(trip, vm.DetailTask);
        Assert.Same(go, vm.SelectedRow);
    }

    [Fact]
    public async Task Delete_and_Space_do_nothing_while_a_Subtask_row_is_selected()
    {
        var (service, vm, trip) = await StartAsync();
        vm.SelectedTask = RowOf(vm, "other"); // an earlier selection they must not fall back to
        vm.SelectedRow = trip.SubtaskRows[0];

        await vm.DeleteSelectedCommand.ExecuteAsync(null);
        await vm.ToggleSelectedCompleteCommand.ExecuteAsync(null);

        Assert.Equal(["trip", "other"], service.All.Select(t => t.Title));
        Assert.All(service.All, t => Assert.False(t.IsCompleted));
        Assert.Equal(["trip", "other"], vm.Tasks.Select(t => t.Title));
        Assert.False(vm.IsUndoBarVisible);
        Assert.Same(trip.SubtaskRows[0], vm.SelectedRow);
    }

    [Fact]
    public async Task Closing_a_Task_while_one_of_its_Subtask_rows_is_selected_selects_the_Task()
    {
        var (_, vm, trip) = await StartAsync();
        vm.SelectedRow = trip.SubtaskRows[1];

        vm.CloseTask(trip);

        Assert.Same(trip, vm.SelectedRow);
        Assert.Null(vm.SelectedSubtaskRow);

        // The chevron does the same.
        vm.OpenTask(trip);
        vm.SelectedRow = trip.SubtaskRows[0];
        vm.ToggleTaskOpenCommand.Execute(trip);
        Assert.False(trip.IsOpen);
        Assert.Same(trip, vm.SelectedRow);
    }

    [Fact]
    public async Task Closing_another_Task_keeps_the_selected_Subtask_row()
    {
        var service = new FakeTaskService();
        var a = await service.CreateTaskAsync("a", Ct);
        var b = await service.CreateTaskAsync("b", Ct);
        await service.AddSubtaskAsync(a.Id, "a1", Ct);
        await service.AddSubtaskAsync(b.Id, "b1", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());
        await vm.LoadAsync(Ct);
        vm.OpenTask(RowOf(vm, "a"));
        vm.OpenTask(RowOf(vm, "b"));
        var a1 = RowOf(vm, "a").SubtaskRows[0];
        vm.SelectedRow = a1;

        vm.CloseTask(RowOf(vm, "b"));

        Assert.Same(a1, vm.SelectedRow);
    }

    [Fact]
    public async Task A_selected_Subtask_row_follows_its_block_into_Completed_N_and_back()
    {
        var (_, vm, trip) = await StartAsync();
        vm.IsCompletedExpanded = true;
        var go = trip.SubtaskRows[1];
        vm.SelectedRow = go;

        await vm.ToggleCompleteCommand.ExecuteAsync(trip);
        await vm.CompletionMove;

        Assert.Contains(go, vm.Groups.Single(g => g.Kind == TaskGroupKind.Completed));
        Assert.Same(go, vm.SelectedRow);
        Assert.Same(trip, vm.SelectedTask);

        // Collapsed, the row is hidden but stays the selection; it is the selection again when it shows.
        vm.IsCompletedExpanded = false;
        Assert.Same(go, vm.SelectedRow);
        vm.IsCompletedExpanded = true;
        Assert.Same(go, vm.SelectedRow);

        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Contains(go, vm.Groups.Single(g => g.Kind == TaskGroupKind.Open));
        Assert.Same(go, vm.SelectedRow);
    }

    [Fact]
    public async Task A_reload_selects_the_same_Subtask_row_again()
    {
        var (_, vm, trip) = await StartAsync();
        var go = trip.SubtaskRows[1];
        vm.SelectedRow = go;

        await vm.LoadAsync(Ct);

        Assert.Equal(go.Id, (vm.SelectedRow as SubtaskListRowViewModel)?.Id);
        Assert.Equal(trip.Id, vm.SelectedTask?.Id);
    }

    [Fact]
    public async Task Deleting_the_selected_Subtask_row_s_Subtask_selects_its_Task()
    {
        var (_, vm, trip) = await StartAsync();
        vm.SelectedRow = trip.SubtaskRows[1];
        var pane = vm.SubtaskItems.Single(s => s.Title == "go");

        await vm.DeleteSubtaskCommand.ExecuteAsync(pane);

        Assert.Same(trip, vm.SelectedRow);
        Assert.Null(vm.SelectedSubtaskRow);
    }
}
