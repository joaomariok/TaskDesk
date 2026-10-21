using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

public class NotifyAtDueViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Thursday = new(2026, 10, 22);

    // Stores the Due and "Notify at Due" on the FakeTaskService's Tasks with the Core rules.
    private sealed class FakeServices(FakeTaskService tasks, FakeTimeProvider clock) : IReminderService, IDueService
    {
        public List<(Guid TaskId, bool NotifyAtDue)> Switched { get; } = [];

        public Task<TaskItem> SetNotifyAtDueAsync(
            Guid taskId, bool notifyAtDue, CancellationToken cancellationToken = default)
        {
            Switched.Add((taskId, notifyAtDue));
            return Change(taskId, t => ReminderCalculator.SetNotifyAtDue(t, notifyAtDue, clock.GetUtcNow()));
        }

        public Task<TaskItem> SetNagPeriodAsync(
            Guid taskId, NagPeriod? period, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaskItem> SetDueAsync(
            Guid taskId, DateOnly? dueDate, TimeOnly? dueTime, CancellationToken cancellationToken = default) =>
            Change(taskId, t => ReminderCalculator.ChangeDue(t, dueDate, dueTime, clock.GetUtcNow()));

        private Task<TaskItem> Change(Guid taskId, Action<TaskItem> change)
        {
            var task = FakeTaskService.With(tasks.All.Single(t => t.Id == taskId), change);
            tasks.Put(task);
            return Task.FromResult(task);
        }

        public Task<SmartViewCounts> CountSmartViewsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SmartViewCounts(0, 0, 0, tasks.All.Count));

        public Task<IReadOnlyList<TaskItem>> ListSmartViewAsync(
            SmartView view, int skip = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>([.. tasks.All.Where(t => !t.IsCompleted)]);

        public Task<TaskItem> AddReminderAsync(
            Guid taskId, ReminderValue reminder, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaskItem> UpdateReminderAsync(
            Guid reminderId, ReminderValue reminder, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaskItem> RemoveReminderAsync(Guid reminderId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaskItem> SnoozeAsync(Guid taskId, TimeSpan duration, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaskItem> ClearSnoozeAsync(Guid taskId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DueReminder>> ListDueAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryMarkFiredAsync(Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DateTime?> TryClaimAsync(Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ReleaseClaimAsync(Guid taskId, DateTime firedUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSearch(FakeTaskService tasks) : ITaskSearchService
    {
        public Task<IReadOnlyList<TaskItem>> SearchAsync(TaskSearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>([.. tasks.All]);
    }

    private sealed record Setup(FakeTaskService Tasks, FakeServices Services, MainViewModel Vm);

    // Tasks "a" (due on Thursday, Notify at Due on) and "b" (due on Thursday, Notify at Due off); "a" is selected.
    private static async Task<Setup> StartAsync()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        var tasks = new FakeTaskService();
        var services = new FakeServices(tasks, clock);
        foreach (var title in new[] { "a", "b" })
        {
            var created = await tasks.CreateTaskAsync(title, Ct);
            await services.SetDueAsync(created.Id, Thursday, null, Ct);
        }

        await services.SetNotifyAtDueAsync(tasks.All.Single(t => t.Title == "b").Id, false, Ct);
        services.Switched.Clear();
        var vm = new MainViewModel(
            tasks, new FakeTagService(tasks), clock, services, new FakeSearch(tasks), reminderService: services);
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks.Single(t => t.Title == "a");
        return new Setup(tasks, services, vm);
    }

    [Fact]
    public async Task The_switch_shows_the_selected_Task_s_Notify_at_Due()
    {
        var s = await StartAsync();

        Assert.True(s.Vm.DetailNotifyAtDue);
        s.Vm.SelectedTask = s.Vm.Tasks.Single(t => t.Title == "b");

        Assert.False(s.Vm.DetailNotifyAtDue);
        Assert.Empty(s.Services.Switched); // loading a Task saves nothing
    }

    [Fact]
    public async Task Switching_it_saves_at_once()
    {
        var s = await StartAsync();
        var a = s.Vm.SelectedTask!;

        s.Vm.DetailNotifyAtDue = false;
        await s.Vm.PendingSave;

        Assert.Equal([(a.Id, false)], s.Services.Switched);
        Assert.False(s.Tasks.All.Single(t => t.Id == a.Id).NotifyAtDue);
        Assert.False(a.Item.NotifyAtDue);
    }

    [Fact]
    public async Task Clearing_the_Due_keeps_the_switch()
    {
        var s = await StartAsync();
        s.Vm.DetailNotifyAtDue = false;
        await s.Vm.PendingSave;

        s.Vm.DetailDueDate = null;
        await s.Vm.PendingSave;
        Assert.False(s.Vm.HasDetailDue);
        s.Vm.DetailDueDate = Thursday.AddDays(1).ToDateTime(TimeOnly.MinValue);
        await s.Vm.PendingSave;

        Assert.False(s.Vm.DetailNotifyAtDue);
        Assert.False(s.Vm.SelectedTask!.Item.NotifyAtDue);
    }
}
