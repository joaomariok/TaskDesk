using System.Globalization;
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

    // Day names follow the current culture, as the row's text does.
    private static string Weekday(int day) => Day(day).ToString("ddd", CultureInfo.CurrentCulture);

    private static string Full(DateOnly date) => date.ToString("ddd d MMM", CultureInfo.CurrentCulture);

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
        Assert.Equal("Yesterday", rows["late"].DueText);
        Assert.False(rows["timed"].IsOverdue);
        Assert.Equal("Today 15:30", rows["timed"].DueText);
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
        Assert.Equal($"{Weekday(25)} 09:00", row.DueText);
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

    // ---- Refresh at local midnight and when a timed Due passes ----

    [Fact]
    public async Task A_timed_Due_turns_Overdue_once_its_time_passes()
    {
        var (service, _, vm, clock) = Start();
        await AddAsync(service, "call", Day(20), new TimeOnly(11, 0));
        await vm.LoadAsync(Ct);
        var row = vm.Tasks.Single();
        Assert.False(row.IsOverdue);

        clock.Advance(TimeSpan.FromMinutes(59));
        await vm.DueRefresh;
        Assert.False(row.IsOverdue);

        clock.Advance(TimeSpan.FromMinutes(1)); // exactly 11:00: Overdue only strictly after the Due time
        await vm.DueRefresh;
        Assert.False(row.IsOverdue);

        clock.Advance(TimeSpan.FromSeconds(1));
        await vm.DueRefresh;
        Assert.True(row.IsOverdue);
    }

    [Fact]
    public async Task At_local_midnight_a_date_only_Due_of_today_turns_Overdue()
    {
        var (service, _, vm, clock) = Start();
        await AddAsync(service, "pay", Day(20));
        await vm.LoadAsync(Ct);

        clock.Advance(TimeSpan.FromHours(14) - TimeSpan.FromSeconds(1)); // 23:59:59 local
        await vm.DueRefresh;
        Assert.False(vm.Tasks.Single().IsOverdue);

        clock.Advance(TimeSpan.FromSeconds(1));
        await vm.DueRefresh;
        Assert.True(vm.Tasks.Single().IsOverdue);
    }

    [Fact]
    public async Task At_local_midnight_the_Today_view_takes_in_the_new_day_and_keeps_doing_so()
    {
        var (service, _, vm, clock) = Start();
        await AddAsync(service, "today", Day(20));
        await AddAsync(service, "tomorrow", Day(21));
        await AddAsync(service, "after", Day(22));
        await vm.ShowSmartViewAsync(SmartView.Today, Ct);
        Assert.Equal(["today"], vm.Tasks.Select(t => t.Title));

        clock.Advance(TimeSpan.FromHours(14));
        await vm.DueRefresh;

        Assert.Equal(["today"], vm.OverdueTasks.Select(t => t.Title));
        Assert.Equal(["tomorrow"], vm.Tasks.Select(t => t.Title));

        clock.Advance(TimeSpan.FromDays(1));
        await vm.DueRefresh;

        Assert.Equal(["today", "tomorrow"], vm.OverdueTasks.Select(t => t.Title));
        Assert.Equal(["after"], vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task In_the_Today_view_a_timed_Due_moves_to_the_Overdue_group_when_it_passes()
    {
        var (service, _, vm, clock) = Start();
        await AddAsync(service, "call", Day(20), new TimeOnly(10, 30));
        await vm.ShowSmartViewAsync(SmartView.Today, Ct);
        Assert.Empty(vm.OverdueTasks);

        clock.Advance(TimeSpan.FromMinutes(31));
        await vm.DueRefresh;

        Assert.Equal(["call"], vm.OverdueTasks.Select(t => t.Title));
        Assert.Empty(vm.Tasks);
        Assert.Equal("Overdue (1)", vm.OverdueHeader);
    }

    [Fact]
    public async Task A_Due_set_in_the_pane_schedules_its_own_refresh()
    {
        var (service, _, vm, clock) = Start();
        await AddAsync(service, "call", null);
        await vm.LoadAsync(Ct);
        var row = vm.Tasks.Single();
        vm.SelectedTask = row;

        vm.DetailDueDate = new DateTime(2026, 10, 20);
        vm.DetailDueTime = "10:15";
        await vm.PendingSave;
        Assert.False(row.IsOverdue);

        clock.Advance(TimeSpan.FromMinutes(16));
        await vm.DueRefresh;

        Assert.True(row.IsOverdue);
    }

    // ---- Due chip: relative text and state (today is Tue 2026-10-20, 10:00 local) ----

    private static async Task<TaskRowViewModel> RowForAsync(DateOnly? date, TimeOnly? time = null)
    {
        var (service, _, vm, _) = Start();
        await AddAsync(service, "t", date, time);
        await vm.ShowSmartViewAsync(SmartView.All, Ct);
        return vm.Tasks.Single();
    }

    [Fact]
    public async Task A_Task_without_Due_has_no_chip()
    {
        var row = await RowForAsync(null);

        Assert.Equal("", row.DueText);
        Assert.Equal(DueState.None, row.DueState);
        Assert.False(row.HasDue);
    }

    [Theory]
    [InlineData(19, "Yesterday", DueState.Overdue)]
    [InlineData(20, "Today", DueState.Today)]
    [InlineData(21, "Tomorrow", DueState.Later)]
    public async Task Yesterday_today_and_tomorrow_read_as_words(int day, string text, DueState state)
    {
        var row = await RowForAsync(Day(day));

        Assert.Equal(text, row.DueText);
        Assert.Equal(state, row.DueState);
        Assert.True(row.HasDue);
    }

    [Theory]
    [InlineData(14)] // six days before
    [InlineData(18)]
    [InlineData(22)]
    [InlineData(26)] // six days after
    public async Task Within_six_days_either_side_the_Due_reads_as_a_weekday(int day)
    {
        var row = await RowForAsync(Day(day));

        Assert.Equal(Weekday(day), row.DueText);
    }

    [Theory]
    [InlineData(13)] // seven days before
    [InlineData(27)] // seven days after
    public async Task Seven_days_away_the_Due_reads_as_a_date_without_the_year(int day)
    {
        var row = await RowForAsync(Day(day));

        Assert.Equal(Full(Day(day)), row.DueText);
    }

    [Fact]
    public async Task A_Due_in_another_year_shows_the_year()
    {
        var next = new DateOnly(2027, 1, 5);
        var previous = new DateOnly(2025, 12, 31);

        Assert.Equal($"{Full(next)} 2027", (await RowForAsync(next)).DueText);
        Assert.Equal($"{Full(previous)} 2025", (await RowForAsync(previous)).DueText);
    }

    [Fact]
    public async Task A_timed_Due_appends_the_time()
    {
        Assert.Equal("Today 18:00", (await RowForAsync(Day(20), new TimeOnly(18, 0))).DueText);
        Assert.Equal("Tomorrow 09:05", (await RowForAsync(Day(21), new TimeOnly(9, 5))).DueText);
        Assert.Equal($"{Weekday(23)} 23:59", (await RowForAsync(Day(23), new TimeOnly(23, 59))).DueText);
        Assert.Equal($"{Full(Day(30))} 07:30", (await RowForAsync(Day(30), new TimeOnly(7, 30))).DueText);
    }

    [Fact]
    public async Task A_timed_Due_today_that_has_passed_is_Overdue_but_still_reads_Today()
    {
        var row = await RowForAsync(Day(20), new TimeOnly(9, 0));

        Assert.Equal(DueState.Overdue, row.DueState);
        Assert.Equal("Today 09:00", row.DueText);
    }

    [Fact]
    public async Task A_past_date_only_Due_is_Overdue()
    {
        var row = await RowForAsync(Day(19));

        Assert.Equal(DueState.Overdue, row.DueState);
    }

    [Fact]
    public async Task A_completed_Task_with_a_past_Due_is_not_marked_Overdue()
    {
        var (service, _, vm, _) = Start();
        var task = await AddAsync(service, "done", Day(19));
        task.IsCompleted = true;
        await vm.ShowSmartViewAsync(SmartView.Completed, Ct);

        Assert.Equal(DueState.Later, vm.CompletedTasks.Single().DueState);
    }

    [Fact]
    public async Task The_chip_text_and_state_follow_the_day_and_the_clock()
    {
        var (service, _, vm, clock) = Start();
        await AddAsync(service, "call", Day(21), new TimeOnly(10, 0));
        await vm.ShowSmartViewAsync(SmartView.All, Ct);
        var row = vm.Tasks.Single();
        Assert.Equal("Tomorrow 10:00", row.DueText);
        Assert.Equal(DueState.Later, row.DueState);

        clock.Advance(TimeSpan.FromHours(14)); // local midnight
        await vm.DueRefresh;
        Assert.Equal("Today 10:00", row.DueText);
        Assert.Equal(DueState.Today, row.DueState);

        clock.Advance(TimeSpan.FromHours(10) + TimeSpan.FromSeconds(1)); // just after 10:00
        await vm.DueRefresh;
        Assert.Equal(DueState.Overdue, row.DueState);

        clock.Advance(TimeSpan.FromDays(1));
        await vm.DueRefresh;
        Assert.Equal("Yesterday 10:00", row.DueText);
        Assert.Equal(DueState.Overdue, row.DueState);
    }

    [Fact]
    public async Task Setting_a_Due_in_the_pane_updates_the_chip()
    {
        var (service, _, vm, _) = Start();
        await AddAsync(service, "call", null);
        await vm.LoadAsync(Ct);
        var row = vm.Tasks.Single();
        vm.SelectedTask = row;

        vm.DetailDueDate = new DateTime(2026, 10, 21);
        await vm.PendingSave;

        Assert.Equal("Tomorrow", row.DueText);
        Assert.Equal(DueState.Later, row.DueState);
    }
}
