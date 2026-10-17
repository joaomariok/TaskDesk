using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Tasks;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

public class SidebarCountsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test-3", TimeSpan.FromHours(-3), "Test-3", "Test-3");

    // Smart Views computed with the Core predicates over the Tasks of the FakeTaskService.
    private sealed class CountingDueService(FakeTaskService tasks, FakeTimeProvider clock) : IDueService
    {
        public Task<TaskItem> SetDueAsync(
            Guid taskId, DateOnly? dueDate, TimeOnly? dueTime, CancellationToken cancellationToken = default)
        {
            var task = tasks.All.Single(t => t.Id == taskId);
            task.DueDate = dueDate;
            task.DueTime = dueTime;
            return Task.FromResult(task);
        }

        public Task<IReadOnlyList<TaskItem>> ListSmartViewAsync(
            SmartView view, int skip = 0, CancellationToken cancellationToken = default)
        {
            var now = clock.GetUtcNow();
            var zone = clock.LocalTimeZone;
            IEnumerable<TaskItem> result = view switch
            {
                SmartView.Today => tasks.All.Where(t => DueCalculator.IsInToday(t.DueDate, t.IsCompleted, now, zone)),
                SmartView.Upcoming => tasks.All.Where(t => DueCalculator.IsUpcoming(t.DueDate, t.IsCompleted, now, zone)),
                SmartView.Overdue => tasks.All.Where(t =>
                    DueCalculator.IsOverdue(t.DueDate, t.DueTime, t.IsCompleted, now, zone)),
                SmartView.All => tasks.All.Where(t => !t.IsCompleted),
                _ => tasks.All.Where(t => t.IsCompleted),
            };
            return Task.FromResult<IReadOnlyList<TaskItem>>([.. result]);
        }

        public async Task<SmartViewCounts> CountSmartViewsAsync(CancellationToken cancellationToken = default)
            => new(
                (await ListSmartViewAsync(SmartView.Today, 0, cancellationToken)).Count,
                (await ListSmartViewAsync(SmartView.Upcoming, 0, cancellationToken)).Count,
                (await ListSmartViewAsync(SmartView.Overdue, 0, cancellationToken)).Count,
                (await ListSmartViewAsync(SmartView.All, 0, cancellationToken)).Count);
    }

    // Local time is UTC-3: 2026-10-20 10:00.
    private static (FakeTaskService Service, MainViewModel Vm) Start(bool withDueService = true)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(Zone);
        var service = new FakeTaskService();
        var vm = new MainViewModel(
            service, new FakeTagService(service), clock, withDueService ? new CountingDueService(service, clock) : null);
        return (service, vm);
    }

    private static async Task AddAsync(FakeTaskService service, DateOnly? date, TimeOnly? time = null, bool done = false)
    {
        var task = await service.CreateTaskAsync("task", Ct);
        task.DueDate = date;
        task.DueTime = time;
        task.IsCompleted = done;
    }

    [Fact]
    public async Task The_counts_start_at_zero_and_the_Overdue_badge_is_hidden()
    {
        var (_, vm) = Start();

        await vm.RefreshSmartViewCountsAsync(Ct);

        Assert.Equal(0, vm.OverdueCount);
        Assert.False(vm.HasOverdueCount);
    }

    [Fact]
    public async Task Each_Smart_View_counts_its_open_Tasks()
    {
        var (service, vm) = Start();
        await AddAsync(service, new DateOnly(2026, 10, 18));                         // Overdue
        await AddAsync(service, new DateOnly(2026, 10, 19));                         // Overdue
        await AddAsync(service, new DateOnly(2026, 10, 20), new TimeOnly(18, 0));    // Today, not yet due
        await AddAsync(service, new DateOnly(2026, 10, 22));                         // Upcoming
        await AddAsync(service, null);                                               // No Due
        await AddAsync(service, new DateOnly(2026, 10, 19), done: true);             // Completed

        await vm.RefreshSmartViewCountsAsync(Ct);

        Assert.Equal(2, vm.OverdueCount);
        Assert.True(vm.HasOverdueCount);
        Assert.Equal(1, vm.UpcomingCount);
        Assert.Equal(5, vm.AllCount);
        Assert.True(vm.TodayCount >= 1);
    }

    [Fact]
    public async Task A_Task_that_stops_being_overdue_clears_the_badge_on_the_next_refresh()
    {
        var (service, vm) = Start();
        await AddAsync(service, new DateOnly(2026, 10, 19));
        await vm.RefreshSmartViewCountsAsync(Ct);
        Assert.Equal(1, vm.OverdueCount);

        service.All.Single().DueDate = new DateOnly(2026, 10, 25);
        await vm.RefreshSmartViewCountsAsync(Ct);

        Assert.Equal(0, vm.OverdueCount);
        Assert.False(vm.HasOverdueCount);
    }

    [Fact]
    public async Task Refreshing_without_a_Due_service_leaves_the_counts_alone()
    {
        var (_, vm) = Start(withDueService: false);

        await vm.RefreshSmartViewCountsAsync(Ct);

        Assert.Equal(0, vm.OverdueCount);
    }

    [Fact]
    public async Task Changing_a_Due_date_asks_the_sidebar_to_recount()
    {
        var (service, vm) = Start();
        await AddAsync(service, null);
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks.Single();
        var signals = 0;
        vm.OpenTaskCountsChanged += (_, _) => signals++;

        vm.DetailDueDate = new DateTime(2026, 10, 19);
        await vm.PendingSave;

        Assert.True(signals > 0);
    }
}
