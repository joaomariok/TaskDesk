using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tasks;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

public class ReminderViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test-3", TimeSpan.FromHours(-3), "Test-3", "Test-3");

    private static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    // Stores the Reminder on the FakeTaskService's Tasks with the Core re-arm rule.
    private sealed class FakeTaskReminders(FakeTaskService tasks) : IReminderService
    {
        public List<(Guid TaskId, DateTime? ReminderUtc)> SetCalls { get; } = [];

        public Task<TaskItem> SetReminderAsync(Guid taskId, DateTime? reminderUtc, CancellationToken cancellationToken = default)
        {
            SetCalls.Add((taskId, reminderUtc));
            var task = FakeTaskService.With(tasks.All.Single(t => t.Id == taskId), t => ReminderCalculator.SetPlanned(t, reminderUtc));
            tasks.Put(task);
            return Task.FromResult(task);
        }

        public Task<IReadOnlyList<DueReminder>> ListDueAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryMarkFiredAsync(Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    // Only the All and Completed views, which showing a Task from a notification falls back to.
    private sealed class FakeViews(FakeTaskService tasks) : IDueService
    {
        public Task<TaskItem> SetDueAsync(
            Guid taskId, DateOnly? dueDate, TimeOnly? dueTime, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TaskItem>> ListSmartViewAsync(
            SmartView view, int skip = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>(view switch
            {
                SmartView.All => [.. tasks.All.Where(t => !t.IsCompleted)],
                SmartView.Completed => [.. tasks.All.Where(t => t.IsCompleted)],
                _ => [],
            });
    }

    private sealed class FakeSearch(FakeTaskService tasks) : ITaskSearchService
    {
        public List<TaskSearchQuery> Queries { get; } = [];

        public Task<IReadOnlyList<TaskItem>> SearchAsync(TaskSearchQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<TaskItem>>(
                [.. tasks.All.Where(t => !query.HasReminder || t.ReminderUtc != null)]);
        }
    }

    private sealed class FakeWindow : IMainWindow
    {
        public bool IsVisible { get; private set; }
        public int FocusCount { get; private set; }

        public void ShowAndFocus()
        {
            IsVisible = true;
            FocusCount++;
        }

        public void Hide() => IsVisible = false;
    }

    private sealed record Setup(FakeTaskService Tasks, FakeTaskReminders Reminders, FakeSearch Search, MainViewModel Vm);

    private static async Task<Setup> StartAsync(params string[] titles)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(Zone);
        var tasks = new FakeTaskService();
        foreach (var title in titles)
        {
            await tasks.CreateTaskAsync(title, Ct);
        }

        var reminders = new FakeTaskReminders(tasks);
        var search = new FakeSearch(tasks);
        var vm = new MainViewModel(
            tasks, new FakeTagService(tasks), clock, new FakeViews(tasks), search, reminderService: reminders);
        await vm.LoadAsync(Ct);
        return new Setup(tasks, reminders, search, vm);
    }

    // ---- Detail pane ----

    [Fact]
    public async Task Selecting_a_Task_shows_its_Reminder_as_local_date_and_time()
    {
        var s = await StartAsync("a");
        await s.Reminders.SetReminderAsync(s.Tasks.All[0].Id, Utc(21, 1, 15), Ct);
        await s.Vm.LoadAsync(Ct);

        s.Vm.SelectedTask = s.Vm.Tasks[0];

        Assert.Equal(new DateTime(2026, 10, 20), s.Vm.DetailReminderDate);
        Assert.Equal("22:15", s.Vm.DetailReminderTime);
    }

    [Fact]
    public async Task A_Task_without_a_Reminder_shows_empty_fields()
    {
        var s = await StartAsync("a");

        s.Vm.SelectedTask = s.Vm.Tasks[0];

        Assert.Null(s.Vm.DetailReminderDate);
        Assert.Equal("", s.Vm.DetailReminderTime);
    }

    [Fact]
    public async Task A_date_only_Reminder_is_saved_at_09_00_local_and_shows_that_time_without_a_Due_date()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.DetailReminderDate = new DateTime(2026, 10, 22);
        await s.Vm.PendingSave;

        Assert.Equal([(s.Tasks.All[0].Id, (DateTime?)Utc(22, 12))], s.Reminders.SetCalls);
        Assert.Equal("09:00", s.Vm.DetailReminderTime);
        Assert.Null(s.Tasks.All[0].DueDate);
        Assert.Null(s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task A_Reminder_date_and_time_are_saved_as_the_UTC_instant()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.DetailReminderDate = new DateTime(2026, 10, 22);
        s.Vm.DetailReminderTime = "14:30";
        await s.Vm.PendingSave;

        Assert.Equal(Utc(22, 17, 30), s.Reminders.SetCalls[^1].ReminderUtc);
        Assert.Equal(Utc(22, 17, 30), s.Tasks.All[0].ReminderUtc);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("noon")]
    public async Task An_invalid_Reminder_time_is_reported_and_not_saved(string text)
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        s.Vm.DetailReminderDate = new DateTime(2026, 10, 22);
        await s.Vm.PendingSave;
        var saves = s.Reminders.SetCalls.Count;

        s.Vm.DetailReminderTime = text;
        await s.Vm.PendingSave;

        Assert.Equal(saves, s.Reminders.SetCalls.Count);
        Assert.NotNull(s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task A_Reminder_time_without_a_date_is_reported_and_not_saved()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.DetailReminderTime = "14:30";
        await s.Vm.PendingSave;

        Assert.Empty(s.Reminders.SetCalls);
        Assert.NotNull(s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task Clearing_the_Reminder_date_clears_the_time_and_the_Reminder()
    {
        var s = await StartAsync("a");
        await s.Reminders.SetReminderAsync(s.Tasks.All[0].Id, Utc(22, 17, 30), Ct);
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.DetailReminderDate = null;
        await s.Vm.PendingSave;

        Assert.Equal("", s.Vm.DetailReminderTime);
        Assert.Null(s.Reminders.SetCalls[^1].ReminderUtc);
        Assert.Null(s.Tasks.All[0].ReminderUtc);
    }

    [Fact]
    public async Task Editing_a_fired_Reminder_in_the_pane_re_arms_it()
    {
        var s = await StartAsync("a");
        await s.Reminders.SetReminderAsync(s.Tasks.All[0].Id, Utc(20, 12), Ct);
        s.Tasks.Put(FakeTaskService.With(s.Tasks.All[0], t => t.ReminderFiredUtc = Utc(20, 12)));
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.DetailReminderTime = "15:00";
        await s.Vm.PendingSave;

        Assert.Null(s.Tasks.All[0].ReminderFiredUtc);
        Assert.Equal(Utc(20, 18), s.Tasks.All[0].ReminderUtc);
    }

    // ---- Filter chip ----

    [Fact]
    public async Task The_has_reminder_chip_filters_the_view_and_clear_all_resets_it()
    {
        var s = await StartAsync("plain", "reminded");
        await s.Reminders.SetReminderAsync(s.Tasks.All[1].Id, Utc(22, 12), Ct);

        await s.Vm.ToggleHasReminderFilterCommand.ExecuteAsync(null);

        Assert.True(s.Vm.HasReminderFilter);
        Assert.True(s.Vm.IsFilterActive);
        Assert.False(s.Vm.IsReorderEnabled);
        Assert.True(s.Search.Queries[^1].HasReminder);
        Assert.Equal(["reminded"], s.Vm.Tasks.Select(t => t.Title));

        await s.Vm.ClearAllFiltersCommand.ExecuteAsync(null);

        Assert.False(s.Vm.HasReminderFilter);
        Assert.Equal(["plain", "reminded"], s.Vm.Tasks.Select(t => t.Title));
    }

    // ---- Notification activation ----

    [Fact]
    public async Task Opening_a_notification_selects_its_Task_in_the_current_view_and_shows_the_window()
    {
        var s = await StartAsync("a", "b");
        var window = new FakeWindow();
        s.Vm.IsDetailPaneOpen = false;
        var handler = new ReminderActivationHandler(s.Vm, window);

        await handler.HandleAsync(new NotificationActivation(s.Tasks.All[1].Id, ReminderAction.Open), Ct);

        Assert.Equal("b", s.Vm.SelectedTask?.Title);
        Assert.True(s.Vm.IsDetailPaneVisible);
        Assert.Equal(1, window.FocusCount);
    }

    [Fact]
    public async Task Opening_a_notification_for_a_Task_outside_the_view_switches_to_All()
    {
        var s = await StartAsync("a");
        s.Vm.SearchText = "zzz";
        await s.Vm.ApplySearchCommand.ExecuteAsync(null);
        var other = await s.Tasks.CreateTaskAsync("elsewhere", Ct);

        await new ReminderActivationHandler(s.Vm, new FakeWindow())
            .HandleAsync(new NotificationActivation(other.Id, ReminderAction.Open), Ct);

        Assert.Equal(SmartView.All, s.Vm.CurrentView);
        Assert.Equal("", s.Vm.SearchText);
        Assert.Equal("elsewhere", s.Vm.SelectedTask?.Title);
    }

    [Fact]
    public async Task Opening_a_notification_for_a_completed_Task_shows_it_in_Completed()
    {
        var s = await StartAsync("a");
        var done = await s.Tasks.CreateTaskAsync("done", Ct);
        await s.Tasks.SetCompletedAsync(done.Id, true, Ct);

        await new ReminderActivationHandler(s.Vm, new FakeWindow())
            .HandleAsync(new NotificationActivation(done.Id, ReminderAction.Open), Ct);

        Assert.Equal(SmartView.Completed, s.Vm.CurrentView);
        Assert.Equal("done", s.Vm.SelectedTask?.Title);
    }

    [Fact]
    public async Task Opening_a_notification_for_a_deleted_Task_says_so()
    {
        var s = await StartAsync("a");

        await new ReminderActivationHandler(s.Vm, new FakeWindow())
            .HandleAsync(new NotificationActivation(Guid.NewGuid(), ReminderAction.Open), Ct);

        Assert.Null(s.Vm.SelectedTask);
        Assert.NotNull(s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task Mark_done_completes_the_Task_without_showing_the_window()
    {
        var s = await StartAsync("a");
        var window = new FakeWindow();
        var counts = 0;
        s.Vm.OpenTaskCountsChanged += (_, _) => counts++;

        await new ReminderActivationHandler(s.Vm, window)
            .HandleAsync(new NotificationActivation(s.Tasks.All[0].Id, ReminderAction.MarkDone), Ct);

        Assert.Equal([(s.Tasks.All[0].Id, true)], s.Tasks.Completions);
        Assert.Equal(["a"], s.Vm.CompletedTasks.Select(t => t.Title));
        Assert.Empty(s.Vm.Tasks);
        Assert.Equal(1, counts);
        Assert.Equal(0, window.FocusCount);
    }

    [Fact]
    public async Task Mark_done_for_a_deleted_Task_does_nothing()
    {
        var s = await StartAsync("a");

        await new ReminderActivationHandler(s.Vm, new FakeWindow())
            .HandleAsync(new NotificationActivation(Guid.NewGuid(), ReminderAction.MarkDone), Ct);

        Assert.Equal(["a"], s.Vm.Tasks.Select(t => t.Title));
        Assert.False(s.Tasks.All[0].IsCompleted);
    }

    // ---- Fallback banner ----

    [Fact]
    public async Task The_fallback_banner_shows_the_reason_until_dismissed()
    {
        var s = await StartAsync();
        Assert.False(s.Vm.HasNotificationBanner);

        s.Vm.ShowNotificationBanner("Windows notifications are unavailable.");

        Assert.True(s.Vm.HasNotificationBanner);
        Assert.Equal("Windows notifications are unavailable.", s.Vm.NotificationBanner);

        s.Vm.DismissNotificationBannerCommand.Execute(null);

        Assert.False(s.Vm.HasNotificationBanner);
    }
}
