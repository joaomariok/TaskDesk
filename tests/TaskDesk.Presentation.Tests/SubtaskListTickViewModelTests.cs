using Microsoft.Extensions.Time.Testing;
using TaskDesk.Presentation;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

/// <summary>Ticking a Subtask from its row in the Task list: the command, Space, the progress and the pane.</summary>
public class SubtaskListTickViewModelTests
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
        var row = vm.Tasks.Single(r => r.Title == "trip");
        vm.OpenTask(row);
        return (service, vm, row);
    }

    [Fact]
    public async Task Ticking_a_Subtask_row_saves_it_and_unticking_saves_that_too()
    {
        var (service, vm, trip) = await StartAsync();
        var pack = trip.SubtaskRows[0];

        await vm.ToggleSubtaskCommand.ExecuteAsync(pack);

        Assert.True(service.All[0].Subtasks[0].IsCompleted);
        Assert.False(service.All[0].Subtasks[1].IsCompleted);
        Assert.True(pack.IsCompleted);

        await vm.ToggleSubtaskCommand.ExecuteAsync(pack);

        Assert.False(service.All[0].Subtasks[0].IsCompleted);
        Assert.False(pack.IsCompleted);
    }

    [Fact]
    public async Task A_tick_updates_the_Task_rows_progress()
    {
        var (_, vm, trip) = await StartAsync();
        Assert.Equal("0/2", trip.SubtaskProgress);

        await vm.ToggleSubtaskCommand.ExecuteAsync(trip.SubtaskRows[0]);
        Assert.Equal("1/2", trip.SubtaskProgress);

        await vm.ToggleSubtaskCommand.ExecuteAsync(trip.SubtaskRows[1]);
        Assert.Equal("2/2", trip.SubtaskProgress);
    }

    [Fact]
    public async Task Space_on_a_selected_Subtask_row_ticks_it_without_completing_the_Task()
    {
        var (service, vm, trip) = await StartAsync();
        var go = trip.SubtaskRows[1];
        vm.SelectedRow = go;

        await vm.ToggleSelectedCompleteCommand.ExecuteAsync(null);

        Assert.True(service.All[0].Subtasks[1].IsCompleted);
        Assert.Equal("1/2", trip.SubtaskProgress);
        Assert.False(trip.IsCompleted);
        Assert.Same(go, vm.SelectedRow);

        await vm.ToggleSelectedCompleteCommand.ExecuteAsync(null);
        Assert.False(service.All[0].Subtasks[1].IsCompleted);
    }

    [Fact]
    public async Task The_detail_pane_shows_a_tick_made_in_the_list_at_once()
    {
        var (_, vm, trip) = await StartAsync();
        vm.OpenDetailPane(trip);
        Assert.Same(trip, vm.DetailTask);
        Assert.All(vm.SubtaskItems, s => Assert.False(s.IsCompleted));

        await vm.ToggleSubtaskCommand.ExecuteAsync(trip.SubtaskRows[1]);

        Assert.Equal([false, true], vm.SubtaskItems.Select(s => s.IsCompleted));
    }

    [Fact]
    public async Task A_tick_lands_in_order_with_a_pending_pane_rename_on_the_same_Task()
    {
        var (service, vm, trip) = await StartAsync();
        vm.OpenDetailPane(trip);

        vm.SubtaskItems[0].Title = "pack well"; // queued in the pane
        var ticking = vm.ToggleSubtaskCommand.ExecuteAsync(trip.SubtaskRows[0]);
        vm.SubtaskItems[1].IsCompleted = true; // and another pane edit after the tick
        await ticking;
        await vm.PendingSave;

        Assert.Equal("pack well", service.All[0].Subtasks[0].Title);
        Assert.True(service.All[0].Subtasks[0].IsCompleted);
        Assert.True(service.All[0].Subtasks[1].IsCompleted);
        Assert.Equal("pack well", vm.SubtaskItems[0].Title);
        Assert.Equal([true, true], vm.SubtaskItems.Select(s => s.IsCompleted));
        Assert.Equal("2/2", trip.SubtaskProgress);
    }

    [Fact]
    public async Task Ticking_leaves_the_Task_open_and_completing_the_Task_leaves_the_Subtasks_alone()
    {
        var (service, vm, trip) = await StartAsync();

        await vm.ToggleSubtaskCommand.ExecuteAsync(trip.SubtaskRows[0]);
        await vm.ToggleSubtaskCommand.ExecuteAsync(trip.SubtaskRows[1]);
        Assert.False(trip.IsCompleted); // all ticked does not complete the Task

        await vm.ToggleCompleteCommand.ExecuteAsync(trip);
        Assert.True(trip.IsCompleted);
        Assert.All(service.All[0].Subtasks, s => Assert.True(s.IsCompleted));

        await vm.ToggleCompleteCommand.ExecuteAsync(trip);
        Assert.False(trip.IsCompleted);
        Assert.Equal("2/2", trip.SubtaskProgress);
    }

    [Fact]
    public async Task A_Subtask_under_a_completed_Task_can_be_ticked()
    {
        var (service, vm, trip) = await StartAsync();
        await vm.ToggleCompleteCommand.ExecuteAsync(trip);

        await vm.ToggleSubtaskCommand.ExecuteAsync(trip.SubtaskRows[0]);

        Assert.True(trip.IsCompleted);
        Assert.True(service.All[0].Subtasks[0].IsCompleted);
        Assert.Equal("1/2", trip.SubtaskProgress);
    }

    [Fact]
    public async Task Ctrl_Z_skips_ticks_and_undoes_the_last_delete()
    {
        var (_, vm, trip) = await StartAsync();
        vm.SelectedTask = vm.Tasks.Single(r => r.Title == "other");
        await vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.DoesNotContain(vm.Tasks, r => r.Title == "other");

        await vm.ToggleSubtaskCommand.ExecuteAsync(trip.SubtaskRows[0]);
        await vm.UndoCommand.ExecuteAsync(null);

        Assert.Contains(vm.Tasks, r => r.Title == "other");
        Assert.Equal("1/2", trip.SubtaskProgress); // the tick stays
    }
}
