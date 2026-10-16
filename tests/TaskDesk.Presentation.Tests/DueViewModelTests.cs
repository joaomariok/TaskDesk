using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

public class DueViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test-3", TimeSpan.FromHours(-3), "Test-3", "Test-3");

    private static DateOnly Day(int day) => new(2026, 10, day);

    // Mutates the Tasks held by the FakeTaskService; the Smart Views are computed with the Core predicates.
    private sealed class FakeDueService(FakeTaskService tasks, FakeTimeProvider clock) : IDueService
    {
        public List<(Guid TaskId, DateOnly? Date, TimeOnly? Time)> SetCalls { get; } = [];
        public List<(SmartView View, int Skip)> ListCalls { get; } = [];

        public Task<TaskItem> SetDueAsync(
            Guid taskId, DateOnly? dueDate, TimeOnly? dueTime, CancellationToken cancellationToken = default)
        {
            DueCalculator.ValidateDue(dueDate, dueTime);
            SetCalls.Add((taskId, dueDate, dueTime));
            var task = tasks.All.Single(t => t.Id == taskId);
            task.DueDate = dueDate;
            task.DueTime = dueTime;
            return Task.FromResult(task);
        }

        public Task<IReadOnlyList<TaskItem>> ListSmartViewAsync(
            SmartView view, int skip = 0, CancellationToken cancellationToken = default)
        {
            ListCalls.Add((view, skip));
            var now = clock.GetUtcNow();
            var zone = clock.LocalTimeZone;
            IEnumerable<TaskItem> result = view switch
            {
                SmartView.Today => tasks.All.Where(t => DueCalculator.IsInToday(t.DueDate, t.IsCompleted, now, zone)),
                SmartView.Upcoming => tasks.All.Where(t => DueCalculator.IsUpcoming(t.DueDate, t.IsCompleted, now, zone)),
                SmartView.Overdue => tasks.All.Where(t =>
                    DueCalculator.IsOverdue(t.DueDate, t.DueTime, t.IsCompleted, now, zone)),
                SmartView.All => tasks.All.Where(t => !t.IsCompleted),
                _ => tasks.All.Where(t => t.IsCompleted).OrderByDescending(t => t.CompletedUtc)
                    .Skip(skip).Take(IDueService.CompletedPageSize),
            };
            return Task.FromResult<IReadOnlyList<TaskItem>>(
                [.. result.OrderBy(t => t.DueDate).ThenBy(t => t.DueTime)]);
        }
    }

    // Local time is UTC-3: 2026-10-20 10:00.
    private static (FakeTaskService Service, FakeDueService Due, MainViewModel Vm, FakeTimeProvider Clock)
        Start()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(Zone);
        var service = new FakeTaskService();
        var due = new FakeDueService(service, clock);
        var vm = new MainViewModel(service, new FakeTagService(service), clock, due);
        return (service, due, vm, clock);
    }

    private static async Task<TaskItem> AddAsync(
        FakeTaskService service, string title, DateOnly? date, TimeOnly? time = null)
    {
        var task = await service.CreateTaskAsync(title, Ct);
        task.DueDate = date;
        task.DueTime = time;
        return task;
    }

    [Fact]
    public async Task Smart_Views_turn_reordering_off_and_the_list_turns_it_back_on()
    {
        var (_, _, vm, _) = Start();
        Assert.True(vm.IsReorderEnabled);

        foreach (var view in Enum.GetValues<SmartView>())
        {
            await vm.ShowSmartViewAsync(view, Ct);
            Assert.False(vm.IsReorderEnabled);
        }

        await vm.ShowSmartViewAsync(null, Ct);
        Assert.True(vm.IsReorderEnabled);
    }

    [Fact]
    public async Task Moving_a_Task_is_ignored_in_a_Smart_View()
    {
        var (service, _, vm, _) = Start();
        await AddAsync(service, "a", null);
        await AddAsync(service, "b", null);
        await vm.ShowSmartViewAsync(SmartView.All, Ct);

        await vm.MoveTaskAsync(0, 1, Ct);

        Assert.Equal(["a", "b"], vm.Tasks.Select(t => t.Title));
        Assert.Empty(service.Moves);
    }

    [Fact]
    public async Task Showing_the_Default_List_again_leaves_the_Smart_View()
    {
        var (service, _, vm, _) = Start();
        await vm.ShowSmartViewAsync(SmartView.Today, Ct);

        await vm.FilterByTagAsync(null, Ct);

        Assert.Null(vm.CurrentView);
        Assert.True(vm.IsReorderEnabled);
    }

    [Theory]
    [InlineData(SmartView.Today)]
    [InlineData(SmartView.Upcoming)]
    [InlineData(SmartView.Overdue)]
    [InlineData(SmartView.All)]
    [InlineData(SmartView.Completed)]
    public async Task The_select_command_behind_Ctrl_1_to_5_shows_that_Smart_View(SmartView view)
    {
        var (_, due, vm, _) = Start();

        await vm.SelectSmartViewCommand.ExecuteAsync(view);

        Assert.Equal(view, vm.CurrentView);
        Assert.Contains((view, 0), due.ListCalls);
    }

    [Fact]
    public async Task Today_shows_the_Overdue_Tasks_in_a_separate_group_at_the_top()
    {
        var (service, _, vm, _) = Start(); // local now: 10:00
        await AddAsync(service, "yesterday", Day(19));
        await AddAsync(service, "today 09:00", Day(20), new TimeOnly(9, 0));
        await AddAsync(service, "today 15:00", Day(20), new TimeOnly(15, 0));
        await AddAsync(service, "today date-only", Day(20));
        await AddAsync(service, "tomorrow", Day(21));

        await vm.ShowSmartViewAsync(SmartView.Today, Ct);

        Assert.Equal(["yesterday", "today 09:00"], vm.OverdueTasks.Select(t => t.Title));
        Assert.Equal(["today date-only", "today 15:00"], vm.Tasks.Select(t => t.Title));
        Assert.Equal("Overdue (2)", vm.OverdueHeader);
        Assert.True(vm.HasOverdueTasks);
    }

    [Fact]
    public async Task The_Overdue_view_lists_Overdue_Tasks_without_a_separate_group()
    {
        var (service, _, vm, _) = Start();
        await AddAsync(service, "late", Day(19));
        await AddAsync(service, "today", Day(20));

        await vm.ShowSmartViewAsync(SmartView.Overdue, Ct);

        Assert.Equal(["late"], vm.Tasks.Select(t => t.Title));
        Assert.Empty(vm.OverdueTasks);
    }

    [Fact]
    public async Task Rows_show_the_Due_and_mark_Overdue_ones()
    {
        var (service, _, vm, _) = Start();
        await AddAsync(service, "late", Day(19));
        await AddAsync(service, "timed", Day(20), new TimeOnly(15, 30));
        await AddAsync(service, "none", null);

        await vm.ShowSmartViewAsync(SmartView.All, Ct);

        var rows = vm.Tasks.ToDictionary(r => r.Title);
        Assert.True(rows["late"].IsOverdue);
        Assert.Equal(Day(19).ToString("d"), rows["late"].DueText);
        Assert.False(rows["timed"].IsOverdue);
        Assert.Equal($"{Day(20):d} {new TimeOnly(15, 30):t}", rows["timed"].DueText);
        Assert.False(rows["none"].IsOverdue);
        Assert.Equal("", rows["none"].DueText);
    }

    [Fact]
    public async Task Quick_add_in_Today_adds_to_the_Default_List_and_sets_Due_to_today()
    {
        var (service, due, vm, _) = Start();
        await vm.ShowSmartViewAsync(SmartView.Today, Ct);
        vm.QuickAddTitle = "Buy milk";

        await vm.AddTaskCommand.ExecuteAsync(null);

        Assert.Equal(["Buy milk"], service.Created);
        var call = Assert.Single(due.SetCalls);
        Assert.Equal((Day(20), (TimeOnly?)null), (call.Date!.Value, call.Time));
        Assert.Equal(["Buy milk"], vm.Tasks.Select(t => t.Title));
        Assert.Equal("", vm.QuickAddTitle);
    }

    [Theory]
    [InlineData(SmartView.Upcoming)]
    [InlineData(SmartView.Overdue)]
    [InlineData(SmartView.All)]
    [InlineData(SmartView.Completed)]
    public async Task Quick_add_in_another_Smart_View_adds_to_the_Default_List_without_a_Due(SmartView view)
    {
        var (service, due, vm, _) = Start();
        await vm.ShowSmartViewAsync(view, Ct);
        vm.QuickAddTitle = "Buy milk";

        await vm.AddTaskCommand.ExecuteAsync(null);

        Assert.Equal(["Buy milk"], service.Created);
        Assert.Empty(due.SetCalls);
        Assert.Null(service.All.Single().DueDate);
    }

    [Fact]
    public async Task Completed_view_loads_100_at_a_time_and_offers_more_until_the_end()
    {
        var (service, due, vm, _) = Start();
        for (var i = 0; i < 150; i++)
        {
            var task = await AddAsync(service, $"t{i:000}", null);
            task.IsCompleted = true;
            task.CompletedUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i);
        }

        await vm.ShowSmartViewAsync(SmartView.Completed, Ct);

        Assert.Equal(100, vm.CompletedTasks.Count);
        Assert.Equal("t149", vm.CompletedTasks[0].Title);
        Assert.True(vm.HasMoreCompleted);
        Assert.True(vm.IsCompletedExpanded);

        await vm.LoadMoreCompletedCommand.ExecuteAsync(null);

        Assert.Equal(150, vm.CompletedTasks.Count);
        Assert.Equal("t000", vm.CompletedTasks[^1].Title);
        Assert.False(vm.HasMoreCompleted);
        Assert.Contains((SmartView.Completed, 100), due.ListCalls);
    }

    [Fact]
    public async Task Completed_view_with_fewer_than_100_offers_no_more()
    {
        var (service, _, vm, _) = Start();
        var task = await AddAsync(service, "done", null);
        task.IsCompleted = true;

        await vm.ShowSmartViewAsync(SmartView.Completed, Ct);

        Assert.False(vm.HasMoreCompleted);
    }

    [Fact]
    public async Task Completing_an_Overdue_Task_in_Today_moves_it_out_of_the_Overdue_group()
    {
        var (service, _, vm, _) = Start();
        await AddAsync(service, "late", Day(19));
        await vm.ShowSmartViewAsync(SmartView.Today, Ct);
        var row = vm.OverdueTasks.Single();

        await vm.ToggleCompleteCommand.ExecuteAsync(row);

        Assert.Empty(vm.OverdueTasks);
        Assert.Equal(["late"], vm.CompletedTasks.Select(t => t.Title));
        Assert.False(row.IsOverdue);
    }

    [Fact]
    public async Task Completing_a_Task_keeps_its_Due()
    {
        var (service, _, vm, _) = Start();
        await AddAsync(service, "a", Day(25), new TimeOnly(9, 0));
        await vm.ShowSmartViewAsync(SmartView.All, Ct);
        var row = vm.Tasks.Single();

        await vm.ToggleCompleteCommand.ExecuteAsync(row);

        Assert.Equal(Day(25), row.Item.DueDate);
        Assert.Equal(new TimeOnly(9, 0), row.Item.DueTime);
        Assert.Equal($"{Day(25):d} {new TimeOnly(9, 0):t}", row.DueText);
    }

    [Fact]
    public async Task Changing_the_Due_keeps_the_Subtask_progress_on_the_row()
    {
        var (service, _, vm, _) = Start();
        var task = await AddAsync(service, "a", null);
        await service.AddSubtaskAsync(task.Id, "step", Ct);
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks.Single();
        var row = vm.SelectedTask!;

        vm.DetailDueDate = new DateTime(2026, 10, 25);
        await vm.PendingSave;

        Assert.Equal("0/1", row.SubtaskProgress);
        Assert.Single(vm.SubtaskItems);
    }

    // ---- Detail pane ----

    private static async Task<(FakeDueService Due, MainViewModel Vm, TaskRowViewModel Row)> SelectedAsync(
        DateOnly? date = null, TimeOnly? time = null)
    {
        var (service, due, vm, _) = Start();
        await AddAsync(service, "a", date, time);
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks.Single();
        return (due, vm, vm.SelectedTask!);
    }

    [Fact]
    public async Task Selecting_a_Task_shows_its_Due_in_the_detail_pane()
    {
        var (_, vm, _) = await SelectedAsync(Day(24), new TimeOnly(9, 5));

        Assert.Equal(new DateTime(2026, 10, 24), vm.DetailDueDate);
        Assert.Equal("09:05", vm.DetailDueTime);
    }

    [Fact]
    public async Task Selecting_a_Task_without_a_Due_shows_empty_fields_and_saves_nothing()
    {
        var (due, vm, _) = await SelectedAsync();
        await vm.PendingSave;

        Assert.Null(vm.DetailDueDate);
        Assert.Equal("", vm.DetailDueTime);
        Assert.Empty(due.SetCalls);
    }

    [Fact]
    public async Task Picking_a_Due_date_saves_it()
    {
        var (due, vm, row) = await SelectedAsync();

        vm.DetailDueDate = new DateTime(2026, 10, 25);
        await vm.PendingSave;

        Assert.Equal([(row.Id, (DateOnly?)Day(25), (TimeOnly?)null)], due.SetCalls);
        Assert.Equal(Day(25), row.Item.DueDate);
        Assert.Null(vm.ErrorMessage);
    }

    [Theory]
    [InlineData("14:30", 14, 30)]
    [InlineData("9:05", 9, 5)]
    [InlineData(" 07:00 ", 7, 0)]
    public async Task A_Due_time_is_saved_with_the_Due_date(string text, int hour, int minute)
    {
        var (due, vm, row) = await SelectedAsync(Day(25));

        vm.DetailDueTime = text;
        await vm.PendingSave;

        Assert.Equal([(row.Id, (DateOnly?)Day(25), (TimeOnly?)new TimeOnly(hour, minute))], due.SetCalls);
    }

    [Fact]
    public async Task A_Due_time_without_a_Due_date_is_rejected_and_not_saved()
    {
        var (due, vm, row) = await SelectedAsync();

        vm.DetailDueTime = "14:30";
        await vm.PendingSave;

        Assert.Equal("A Due time needs a Due date.", vm.ErrorMessage);
        Assert.Empty(due.SetCalls);
        Assert.Null(row.Item.DueTime);
    }

    [Fact]
    public async Task A_rejected_Due_time_is_saved_once_a_Due_date_is_picked()
    {
        var (due, vm, row) = await SelectedAsync();
        vm.DetailDueTime = "14:30";

        vm.DetailDueDate = new DateTime(2026, 10, 25);
        await vm.PendingSave;

        Assert.Equal([(row.Id, (DateOnly?)Day(25), (TimeOnly?)new TimeOnly(14, 30))], due.SetCalls);
        Assert.Null(vm.ErrorMessage);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("abc")]
    [InlineData("1430")]
    public async Task An_unreadable_Due_time_is_rejected_and_not_saved(string text)
    {
        var (due, vm, _) = await SelectedAsync(Day(25));

        vm.DetailDueTime = text;
        await vm.PendingSave;

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(due.SetCalls);
    }

    [Fact]
    public async Task Clearing_the_Due_date_clears_the_Due_time_too()
    {
        var (due, vm, row) = await SelectedAsync(Day(25), new TimeOnly(14, 30));

        vm.DetailDueDate = null;
        await vm.PendingSave;

        Assert.Equal("", vm.DetailDueTime);
        Assert.Equal([(row.Id, (DateOnly?)null, (TimeOnly?)null)], due.SetCalls);
    }

    [Fact]
    public async Task Clearing_the_Due_time_keeps_the_Due_date()
    {
        var (due, vm, row) = await SelectedAsync(Day(25), new TimeOnly(14, 30));

        vm.DetailDueTime = "";
        await vm.PendingSave;

        Assert.Equal([(row.Id, (DateOnly?)Day(25), (TimeOnly?)null)], due.SetCalls);
    }

    [Fact]
    public async Task Making_a_Task_overdue_in_Today_moves_it_to_the_Overdue_group()
    {
        var (service, _, vm, _) = Start();
        await AddAsync(service, "a", Day(20));
        await vm.ShowSmartViewAsync(SmartView.Today, Ct);
        vm.SelectedTask = vm.Tasks.Single();
        Assert.Empty(vm.OverdueTasks);

        vm.DetailDueDate = new DateTime(2026, 10, 18);
        await vm.PendingSave;

        Assert.Equal(["a"], vm.OverdueTasks.Select(t => t.Title));
        Assert.Empty(vm.Tasks);
    }
}
