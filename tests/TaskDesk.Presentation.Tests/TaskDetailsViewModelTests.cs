using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

public class TaskDetailsViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(FakeTaskService Service, MainViewModel Vm, FakeTimeProvider Clock)> StartAsync(
        params string[] titles)
    {
        var service = new FakeTaskService();
        foreach (var title in titles)
        {
            await service.CreateTaskAsync(title, Ct);
        }

        var clock = new FakeTimeProvider();
        var vm = new MainViewModel(service, new FakeTagService(service), clock);
        await vm.LoadAsync(Ct);
        return (service, vm, clock);
    }

    [Fact]
    public async Task Completing_a_Task_moves_it_to_the_Completed_group_and_updates_the_count()
    {
        var (service, vm, _) = await StartAsync("a", "b");
        var a = vm.Tasks[0];

        await vm.ToggleCompleteCommand.ExecuteAsync(a);

        Assert.Equal(["b"], vm.Tasks.Select(t => t.Title));
        Assert.Equal(["a"], vm.CompletedTasks.Select(t => t.Title));
        Assert.Equal("Completed (1)", vm.CompletedHeader);
        Assert.True(a.IsCompleted);
        Assert.Equal([(a.Id, true)], service.Completions);
    }

    [Fact]
    public async Task Un_completing_a_Task_returns_it_to_its_place_in_the_List()
    {
        var (_, vm, _) = await StartAsync("a", "b", "c");
        var b = vm.Tasks[1];
        await vm.ToggleCompleteCommand.ExecuteAsync(b);

        await vm.ToggleCompleteCommand.ExecuteAsync(b);

        Assert.Equal(["a", "b", "c"], vm.Tasks.Select(t => t.Title));
        Assert.Empty(vm.CompletedTasks);
        Assert.Equal("Completed (0)", vm.CompletedHeader);
    }

    [Fact]
    public async Task The_most_recently_completed_Task_is_first_in_the_Completed_group()
    {
        var (_, vm, _) = await StartAsync("a", "b");
        await vm.ToggleCompleteCommand.ExecuteAsync(vm.Tasks[0]);
        await vm.ToggleCompleteCommand.ExecuteAsync(vm.Tasks[0]);

        Assert.Equal(["b", "a"], vm.CompletedTasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Toggling_the_selected_Task_is_what_Space_does()
    {
        var (_, vm, _) = await StartAsync("a", "b");
        vm.SelectedTask = vm.Tasks[1];

        await vm.ToggleSelectedCompleteCommand.ExecuteAsync(null);

        Assert.Equal(["b"], vm.CompletedTasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Moving_a_Task_accounts_for_completed_Tasks_in_the_full_order()
    {
        var (service, vm, _) = await StartAsync("a", "x", "b", "c");
        await vm.ToggleCompleteCommand.ExecuteAsync(vm.Tasks[1]); // x completed
        var a = vm.Tasks[0];

        await vm.MoveTaskAsync(0, 2, Ct); // a after c

        Assert.Equal(["b", "c", "a"], vm.Tasks.Select(t => t.Title));
        Assert.Equal([(a.Id, 3)], service.Moves); // full order is a, x, b, c
    }

    [Fact]
    public async Task Ctrl_Z_after_completing_un_completes_the_Task()
    {
        var (_, vm, _) = await StartAsync("a", "b");
        await vm.ToggleCompleteCommand.ExecuteAsync(vm.Tasks[0]);

        await vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal(["a", "b"], vm.Tasks.Select(t => t.Title));
        Assert.Empty(vm.CompletedTasks);
    }

    [Fact]
    public async Task Deleting_removes_the_Task_and_shows_the_Undo_bar_for_ten_seconds()
    {
        var (service, vm, clock) = await StartAsync("a", "b");
        var a = vm.Tasks[0];

        await vm.DeleteCommand.ExecuteAsync(a);

        Assert.Equal(["b"], vm.Tasks.Select(t => t.Title));
        Assert.Equal([a.Id], service.Deleted);
        Assert.True(vm.IsUndoBarVisible);
        Assert.Equal("Task deleted — Undo", MainViewModel.UndoBarMessage);

        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.True(vm.IsUndoBarVisible);

        clock.Advance(TimeSpan.FromSeconds(1));
        await vm.UndoBarExpiry;
        Assert.False(vm.IsUndoBarVisible);
    }

    [Fact]
    public async Task Undo_after_delete_puts_the_Task_back_in_its_place_and_hides_the_bar()
    {
        var (service, vm, _) = await StartAsync("a", "b", "c");
        var b = vm.Tasks[1];
        await vm.DeleteCommand.ExecuteAsync(b);

        await vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal(["a", "b", "c"], vm.Tasks.Select(t => t.Title));
        Assert.Equal(b.Id, vm.Tasks[1].Id);
        Assert.Equal([b.Id], service.Restored);
        Assert.False(vm.IsUndoBarVisible);
    }

    [Fact]
    public async Task Undo_after_deleting_a_completed_Task_restores_it_into_the_Completed_group()
    {
        var (_, vm, _) = await StartAsync("a", "b");
        await vm.ToggleCompleteCommand.ExecuteAsync(vm.Tasks[0]);
        await vm.DeleteCommand.ExecuteAsync(vm.CompletedTasks[0]);
        Assert.Equal("Completed (0)", vm.CompletedHeader);

        await vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal(["a"], vm.CompletedTasks.Select(t => t.Title));
        Assert.Equal(["b"], vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Undo_only_reverts_the_last_action()
    {
        var (service, vm, _) = await StartAsync("a", "b");
        await vm.DeleteCommand.ExecuteAsync(vm.Tasks[0]);
        await vm.ToggleCompleteCommand.ExecuteAsync(vm.Tasks[0]); // b completed; the bar hides

        Assert.False(vm.IsUndoBarVisible);
        await vm.UndoCommand.ExecuteAsync(null);
        await vm.UndoCommand.ExecuteAsync(null); // nothing left to undo

        Assert.Equal(["b"], vm.Tasks.Select(t => t.Title));
        Assert.Empty(service.Restored);
    }

    [Fact]
    public async Task Selecting_a_Task_shows_it_in_the_detail_pane()
    {
        var (service, vm, _) = await StartAsync("a");
        await service.UpdateTaskAsync(vm.Tasks[0].Id, "a", "some notes", Priority.High, Ct);
        await vm.LoadAsync(Ct);

        vm.SelectedTask = vm.Tasks[0];

        Assert.Equal(("a", "some notes", Priority.High), (vm.DetailTitle, vm.DetailNotes, vm.DetailPriority));
        Assert.True(vm.IsDetailPaneVisible);
    }

    [Fact]
    public async Task A_closed_docked_pane_stays_closed_on_the_next_selection_until_an_explicit_open()
    {
        var (_, vm, _) = await StartAsync("a", "b");
        vm.IsDetailDocked = true;
        vm.SelectedTask = vm.Tasks[0];

        vm.CloseDetailPaneCommand.Execute(null);
        Assert.False(vm.IsDetailPaneVisible);

        vm.SelectedTask = vm.Tasks[1]; // an arrow key
        Assert.False(vm.IsDetailPaneVisible);

        vm.OpenDetailPane(); // a click, Enter or F2
        Assert.True(vm.IsDetailPaneVisible);
        Assert.Equal("b", vm.DetailTitle);
    }

    [Fact]
    public async Task An_open_docked_pane_follows_the_selection()
    {
        var (_, vm, _) = await StartAsync("a", "b");
        vm.IsDetailDocked = true;
        vm.OpenDetailPane(vm.Tasks[0]);

        vm.SelectedTask = vm.Tasks[1];

        Assert.True(vm.IsDetailPaneVisible);
        Assert.Equal("b", vm.DetailTitle);
    }

    [Fact]
    public async Task Docking_loads_a_selection_made_while_the_pane_overlaid_the_list()
    {
        var (_, vm, _) = await StartAsync("a");
        vm.IsDetailDocked = false;
        vm.SelectedTask = vm.Tasks[0];
        Assert.Null(vm.DetailTask);

        vm.IsDetailDocked = true;

        Assert.Same(vm.Tasks[0], vm.DetailTask);
        Assert.Equal("a", vm.DetailTitle);
        Assert.True(vm.IsDetailPaneVisible);
    }

    [Fact]
    public async Task Docking_keeps_a_closed_pane_closed()
    {
        var (_, vm, _) = await StartAsync("a", "b");
        vm.IsDetailDocked = false;
        vm.OpenDetailPane(vm.Tasks[0]);
        vm.CloseDetailPaneCommand.Execute(null);
        vm.SelectedTask = vm.Tasks[1];

        vm.IsDetailDocked = true;

        Assert.False(vm.IsDetailPaneVisible);
    }

    [Fact]
    public async Task A_docked_pane_loads_the_selected_Task()
    {
        var (_, vm, _) = await StartAsync("a", "b");
        vm.IsDetailDocked = true;

        vm.SelectedTask = vm.Tasks[0];
        vm.SelectedTask = vm.Tasks[1];

        Assert.Same(vm.Tasks[1], vm.DetailTask);
        Assert.Equal("b", vm.DetailTitle);
        Assert.True(vm.IsDetailPaneVisible);
    }

    [Fact]
    public async Task An_overlaid_pane_does_not_load_a_selection()
    {
        var (_, vm, _) = await StartAsync("a");
        vm.IsDetailDocked = false;

        vm.SelectedTask = vm.Tasks[0];

        Assert.Null(vm.DetailTask);
        Assert.Equal("", vm.DetailTitle);
        Assert.False(vm.IsDetailPaneVisible);
    }

    [Fact]
    public async Task An_overlaid_pane_stays_on_its_Task_until_another_explicit_open()
    {
        var (_, vm, _) = await StartAsync("a", "b");
        vm.IsDetailDocked = false;
        vm.OpenDetailPane(vm.Tasks[0]);

        vm.SelectedTask = vm.Tasks[1];

        Assert.Same(vm.Tasks[0], vm.DetailTask);
        Assert.Equal("a", vm.DetailTitle);
        Assert.True(vm.IsDetailPaneVisible);

        vm.OpenDetailPane(vm.Tasks[1]);

        Assert.Same(vm.Tasks[1], vm.DetailTask);
        Assert.Equal("b", vm.DetailTitle);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_explicit_open_always_loads_the_Task_and_reopens_a_closed_pane(bool docked)
    {
        var (_, vm, _) = await StartAsync("a");
        vm.IsDetailDocked = docked;
        vm.OpenDetailPane(vm.Tasks[0]);
        vm.CloseDetailPaneCommand.Execute(null);
        Assert.False(vm.IsDetailPaneVisible);

        vm.OpenDetailPane(vm.Tasks[0]);

        Assert.True(vm.IsDetailPaneVisible);
        Assert.Equal("a", vm.DetailTitle);
        Assert.Same(vm.Tasks[0], vm.SelectedTask);
    }

    [Fact]
    public async Task Docking_an_overlaid_pane_makes_it_follow_the_selection()
    {
        var (_, vm, _) = await StartAsync("a", "b");
        vm.IsDetailDocked = false;
        vm.OpenDetailPane(vm.Tasks[0]);
        vm.SelectedTask = vm.Tasks[1];

        vm.IsDetailDocked = true;

        Assert.Same(vm.Tasks[1], vm.DetailTask);
    }

    [Fact]
    public async Task Repeat_is_hidden_until_the_Task_has_a_Due_date()
    {
        var (_, vm, _) = await StartAsync("a");
        vm.SelectedTask = vm.Tasks[0];
        Assert.False(vm.HasDetailDue);

        vm.DetailDueDate = new DateTime(2026, 10, 20);
        Assert.True(vm.HasDetailDue);

        vm.DetailDueDate = null;
        Assert.False(vm.HasDetailDue);
    }

    [Fact]
    public async Task Delete_Task_in_the_pane_deletes_the_Task_the_pane_shows()
    {
        var (_, vm, _) = await StartAsync("a", "b");
        vm.IsDetailDocked = false;
        vm.OpenDetailPane(vm.Tasks[0]);
        vm.SelectedTask = vm.Tasks[1];

        await vm.DeleteDetailTaskCommand.ExecuteAsync(null);

        Assert.Equal(["b"], vm.Tasks.Select(t => t.Title));
        Assert.Null(vm.DetailTask);
        Assert.False(vm.IsDetailPaneVisible);
    }

    [Fact]
    public async Task Preparing_for_an_import_saves_the_pending_edit_and_closes_the_pane()
    {
        var (service, vm, clock) = await StartAsync("a");
        var a = vm.Tasks[0];
        vm.SelectedTask = a;
        vm.DetailTitle = "edited";

        await vm.PrepareForImportAsync();

        Assert.Equal([(a.Id, "edited", "", Priority.None)], service.Updates);
        Assert.False(vm.IsDetailPaneOpen);
        Assert.Null(vm.SelectedTask);

        clock.Advance(TimeSpan.FromSeconds(5));
        await vm.PendingSave;
        Assert.Single(service.Updates);
    }

    [Fact]
    public async Task Text_edits_are_saved_once_after_500_ms_of_quiet()
    {
        var (service, vm, clock) = await StartAsync("a");
        var a = vm.Tasks[0];
        vm.SelectedTask = a;

        vm.DetailTitle = "ab";
        clock.Advance(TimeSpan.FromMilliseconds(400));
        vm.DetailTitle = "abc";
        clock.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Empty(service.Updates);

        clock.Advance(TimeSpan.FromMilliseconds(100));
        await vm.PendingSave;

        Assert.Equal([(a.Id, "abc", "", Priority.None)], service.Updates);
        Assert.Equal("abc", a.Title);
    }

    [Fact]
    public async Task Priority_changes_are_saved_immediately_and_show_a_marker_on_the_row()
    {
        var (service, vm, _) = await StartAsync("a");
        var a = vm.Tasks[0];
        vm.SelectedTask = a;
        Assert.Equal("", a.PriorityMarker);

        vm.DetailPriority = Priority.High;
        await vm.PendingSave;

        Assert.Equal([(a.Id, "a", "", Priority.High)], service.Updates);
        Assert.Equal("!!!", a.PriorityMarker);
    }

    [Theory]
    [InlineData(Priority.None, "")]
    [InlineData(Priority.Low, "!")]
    [InlineData(Priority.Medium, "!!")]
    [InlineData(Priority.High, "!!!")]
    public void Priority_marker_has_one_exclamation_mark_per_level(Priority priority, string marker)
    {
        var row = new TaskRowViewModel(new TaskItem { Id = Guid.NewGuid(), Title = "t", Priority = priority });

        Assert.Equal(marker, row.PriorityMarker);
    }

    [Fact]
    public async Task Changing_the_selection_flushes_a_pending_edit_to_the_old_Task()
    {
        var (service, vm, _) = await StartAsync("a", "b");
        var (a, b) = (vm.Tasks[0], vm.Tasks[1]);
        vm.SelectedTask = a;
        vm.DetailNotes = "remember";

        vm.SelectedTask = b;
        await vm.PendingSave;

        Assert.Equal([(a.Id, "a", (string?)"remember", Priority.None)], service.Updates);
        Assert.Equal("", vm.DetailNotes);
    }

    [Fact]
    public async Task Flushing_on_exit_saves_a_pending_edit()
    {
        var (service, vm, _) = await StartAsync("a");
        vm.SelectedTask = vm.Tasks[0];
        vm.DetailTitle = "renamed";

        await vm.FlushPendingSaveAsync();

        Assert.Equal("renamed", Assert.Single(service.Updates).Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_title_is_not_saved(string title)
    {
        var (service, vm, clock) = await StartAsync("a");
        vm.SelectedTask = vm.Tasks[0];

        vm.DetailTitle = title;
        clock.Advance(MainViewModel.SaveDebounce);
        await vm.PendingSave;

        Assert.Empty(service.Updates);
        Assert.Equal("a", vm.Tasks[0].Title);
    }

    [Fact]
    public async Task Deleting_the_selected_Task_discards_its_pending_edit()
    {
        var (service, vm, clock) = await StartAsync("a");
        vm.SelectedTask = vm.Tasks[0];
        vm.DetailTitle = "edited";

        await vm.DeleteSelectedCommand.ExecuteAsync(null);
        clock.Advance(MainViewModel.SaveDebounce);
        await vm.PendingSave;

        Assert.Empty(service.Updates);
        Assert.Null(vm.SelectedTask);
        Assert.Null(vm.ErrorMessage);
    }
}
