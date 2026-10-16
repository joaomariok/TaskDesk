using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

public class SubtasksViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(FakeTaskService Service, MainViewModel Vm)> StartAsync(int subtasks = 0)
    {
        var service = new FakeTaskService();
        var task = await service.CreateTaskAsync("trip", Ct);
        await service.CreateTaskAsync("other", Ct);
        for (var i = 1; i <= subtasks; i++)
        {
            await service.AddSubtaskAsync(task.Id, "step " + i, Ct);
        }

        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks[0];
        return (service, vm);
    }

    [Fact]
    public async Task Selecting_a_Task_lists_its_Subtasks_and_the_row_shows_the_progress()
    {
        var (_, vm) = await StartAsync(subtasks: 3);

        Assert.Equal(["step 1", "step 2", "step 3"], vm.SubtaskItems.Select(s => s.Title));
        Assert.Equal("0/3", vm.Tasks[0].SubtaskProgress);
        Assert.True(vm.Tasks[0].HasSubtasks);
        Assert.Equal("", vm.Tasks[1].SubtaskProgress);
        Assert.False(vm.Tasks[1].HasSubtasks);
    }

    [Fact]
    public async Task Adding_a_Subtask_appends_it_clears_the_input_and_updates_the_progress()
    {
        var (_, vm) = await StartAsync();
        vm.NewSubtaskTitle = "  pack ";

        await vm.AddSubtaskCommand.ExecuteAsync(null);

        Assert.Equal(["pack"], vm.SubtaskItems.Select(s => s.Title));
        Assert.Equal("", vm.NewSubtaskTitle);
        Assert.Equal("0/1", vm.Tasks[0].SubtaskProgress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Adding_is_disabled_for_an_empty_title(string title)
    {
        var (_, vm) = await StartAsync();
        vm.NewSubtaskTitle = title;

        Assert.False(vm.AddSubtaskCommand.CanExecute(null));
        await vm.AddSubtaskCommand.ExecuteAsync(null);

        Assert.Empty(vm.SubtaskItems);
    }

    [Fact]
    public async Task Ticking_a_Subtask_updates_the_progress_but_never_completes_the_Task()
    {
        var (service, vm) = await StartAsync(subtasks: 2);

        foreach (var subtask in vm.SubtaskItems)
        {
            subtask.IsCompleted = true;
        }

        await vm.PendingSave;
        Assert.Equal("2/2", vm.Tasks[0].SubtaskProgress);
        Assert.False(vm.Tasks[0].IsCompleted);
        Assert.Empty(service.Completions);
    }

    [Fact]
    public async Task Completing_the_Task_does_not_tick_its_Subtasks()
    {
        var (_, vm) = await StartAsync(subtasks: 2);
        var row = vm.Tasks[0];

        await vm.ToggleCompleteCommand.ExecuteAsync(row);

        Assert.True(row.IsCompleted);
        Assert.Equal("0/2", row.SubtaskProgress);
        Assert.All(vm.SubtaskItems, s => Assert.False(s.IsCompleted));
    }

    [Fact]
    public async Task Renaming_a_Subtask_saves_it_and_an_empty_title_goes_back_to_the_saved_one()
    {
        var (service, vm) = await StartAsync(subtasks: 1);
        var subtask = vm.SubtaskItems[0];

        subtask.Title = "renamed";
        await vm.PendingSave;
        Assert.Equal("renamed", service.All[0].Subtasks[0].Title);

        subtask.Title = "  ";
        await vm.PendingSave;
        Assert.Equal("renamed", subtask.Title);
        Assert.Equal("renamed", service.All[0].Subtasks[0].Title);
    }

    [Fact]
    public async Task Moving_a_Subtask_up_and_down_reorders_the_list_and_ignores_the_ends()
    {
        var (_, vm) = await StartAsync(subtasks: 3);

        await vm.MoveSubtaskDownCommand.ExecuteAsync(vm.SubtaskItems[0]);
        Assert.Equal(["step 2", "step 1", "step 3"], vm.SubtaskItems.Select(s => s.Title));

        await vm.MoveSubtaskUpCommand.ExecuteAsync(vm.SubtaskItems[2]);
        Assert.Equal(["step 2", "step 3", "step 1"], vm.SubtaskItems.Select(s => s.Title));

        await vm.MoveSubtaskUpCommand.ExecuteAsync(vm.SubtaskItems[0]);
        await vm.MoveSubtaskDownCommand.ExecuteAsync(vm.SubtaskItems[2]);
        Assert.Equal(["step 2", "step 3", "step 1"], vm.SubtaskItems.Select(s => s.Title));
    }

    [Fact]
    public async Task Deleting_a_Subtask_shows_the_Undo_bar_and_Undo_restores_it_in_place()
    {
        var (service, vm) = await StartAsync(subtasks: 3);
        var middle = vm.SubtaskItems[1];

        await vm.DeleteSubtaskCommand.ExecuteAsync(middle);

        Assert.Equal(["step 1", "step 3"], vm.SubtaskItems.Select(s => s.Title));
        Assert.Equal("0/2", vm.Tasks[0].SubtaskProgress);
        Assert.True(vm.IsUndoBarVisible);
        Assert.Equal("Subtask deleted", vm.UndoBarText);

        await vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal(["step 1", "step 2", "step 3"], vm.SubtaskItems.Select(s => s.Title));
        Assert.Equal("0/3", vm.Tasks[0].SubtaskProgress);
        Assert.False(vm.IsUndoBarVisible);
        Assert.Equal([middle.Id], service.RestoredSubtasks);
    }

    [Fact]
    public async Task Undoing_a_Task_delete_brings_back_its_Subtasks_and_progress()
    {
        var (service, vm) = await StartAsync(subtasks: 2);
        vm.SubtaskItems[0].IsCompleted = true;
        await vm.PendingSave;
        var row = vm.Tasks[0];

        await vm.DeleteCommand.ExecuteAsync(row);
        Assert.Equal("Task deleted", vm.UndoBarText);
        await vm.UndoCommand.ExecuteAsync(null);

        var restored = vm.Tasks[0];
        Assert.Equal("trip", restored.Title);
        Assert.Equal("1/2", restored.SubtaskProgress);
        Assert.Equal(2, service.All[0].Subtasks.Count);
    }
}
