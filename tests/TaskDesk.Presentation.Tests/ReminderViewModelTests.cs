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
    private sealed class FakeTaskReminders(FakeTaskService tasks, TimeProvider clock) : IReminderService
    {
        public List<(Guid TaskId, DateTime? ReminderUtc)> SetCalls { get; } = [];
        public List<(Guid TaskId, TimeSpan Duration)> SnoozeCalls { get; } = [];

        public Task<TaskItem> SnoozeAsync(Guid taskId, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            SnoozeCalls.Add((taskId, duration));
            var current = tasks.All.SingleOrDefault(t => t.Id == taskId)
                ?? throw new KeyNotFoundException($"Task {taskId} does not exist.");
            var task = FakeTaskService.With(current, t => ReminderCalculator.Snooze(t, clock.GetUtcNow(), duration));
            tasks.Put(task);
            return Task.FromResult(task);
        }

        public Task<TaskItem> ClearSnoozeAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            var task = FakeTaskService.With(tasks.All.Single(t => t.Id == taskId), t => ReminderCalculator.ClearSnooze(t));
            tasks.Put(task);
            return Task.FromResult(task);
        }

        public Task<TaskItem> SetReminderAsync(Guid taskId, DateTime? reminderUtc, CancellationToken cancellationToken = default)
        {
            SetCalls.Add((taskId, reminderUtc));
            var task = FakeTaskService.With(tasks.All.Single(t => t.Id == taskId), t => ReminderCalculator.SetPlanned(t, reminderUtc, clock.GetUtcNow()));
            tasks.Put(task);
            return Task.FromResult(task);
        }

        public Task<IReadOnlyList<DueReminder>> ListDueAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryMarkFiredAsync(Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DateTime?> TryClaimAsync(Guid taskId, DateTime dueAtUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ReleaseClaimAsync(Guid taskId, DateTime firedUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    // The All and Completed views, which showing a Task from a notification falls back to, and Today.
    private sealed class FakeViews(FakeTaskService tasks) : IDueService
    {
        public Task<TaskItem> SetDueAsync(
            Guid taskId, DateOnly? dueDate, TimeOnly? dueTime, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SmartViewCounts> CountSmartViewsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TaskItem>> ListSmartViewAsync(
            SmartView view, int skip = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>(view switch
            {
                SmartView.Today => [.. tasks.All.Where(t => !t.IsCompleted && t.DueDate is not null)],
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

    private sealed record Setup(
        FakeTaskService Tasks, FakeTaskReminders Reminders, FakeSearch Search, MainViewModel Vm, FakeTimeProvider Clock);

    private static async Task<Setup> StartAsync(params string[] titles)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(Zone);
        var tasks = new FakeTaskService();
        foreach (var title in titles)
        {
            await tasks.CreateTaskAsync(title, Ct);
        }

        var reminders = new FakeTaskReminders(tasks, clock);
        var search = new FakeSearch(tasks);
        var vm = new MainViewModel(
            tasks, new FakeTagService(tasks), clock, new FakeViews(tasks), search, reminderService: reminders);
        await vm.LoadAsync(Ct);
        return new Setup(tasks, reminders, search, vm, clock);
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
        Assert.Equal(new TimeSpan(22, 15, 0), s.Vm.DetailReminderTime);
    }

    [Fact]
    public async Task A_Task_without_a_Reminder_shows_empty_fields()
    {
        var s = await StartAsync("a");

        s.Vm.SelectedTask = s.Vm.Tasks[0];

        Assert.Null(s.Vm.DetailReminderDate);
        Assert.Null(s.Vm.DetailReminderTime);
    }

    [Fact]
    public async Task A_date_only_Reminder_is_saved_at_09_00_local_and_shows_that_time_without_a_Due_date()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.DetailReminderDate = new DateTime(2026, 10, 22);
        await s.Vm.PendingSave;

        Assert.Equal([(s.Tasks.All[0].Id, (DateTime?)Utc(22, 12))], s.Reminders.SetCalls);
        Assert.Equal(new TimeSpan(9, 0, 0), s.Vm.DetailReminderTime);
        Assert.Null(s.Tasks.All[0].DueDate);
        Assert.Null(s.Vm.ErrorMessage);
    }

    private sealed class FakeSettingsStore : IShellSettingsStore
    {
        public ShellSettings Current { get; set; } = new();
        public ShellSettings Load() => Current;
        public void Save(ShellSettings settings) => Current = settings;
    }

    [Fact]
    public async Task A_date_only_Reminder_uses_the_default_Reminder_time_setting_and_follows_changes_to_it()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(Zone);
        var tasks = new FakeTaskService();
        await tasks.CreateTaskAsync("a", Ct);
        await tasks.CreateTaskAsync("b", Ct);
        var reminders = new FakeTaskReminders(tasks, clock);
        var settings = new FakeSettingsStore { Current = new ShellSettings(DefaultReminderTime: new TimeOnly(7, 30)) };
        var vm = new MainViewModel(
            tasks, new FakeTagService(tasks), clock, new FakeViews(tasks), new FakeSearch(tasks),
            reminderService: reminders, settingsStore: settings);
        await vm.LoadAsync(Ct);

        vm.SelectedTask = vm.Tasks[0];
        vm.DetailReminderDate = new DateTime(2026, 10, 22);
        await vm.PendingSave;

        Assert.Equal(Utc(22, 10, 30), reminders.SetCalls[^1].ReminderUtc);
        Assert.Equal(new TimeSpan(7, 30, 0), vm.DetailReminderTime);

        // Changed in Settings while the app runs: the next date-only Reminder uses it, no restart needed.
        settings.Current = settings.Current with { DefaultReminderTime = new TimeOnly(18, 15) };
        vm.SelectedTask = vm.Tasks[1];
        vm.DetailReminderDate = new DateTime(2026, 10, 23);
        await vm.PendingSave;

        Assert.Equal(Utc(23, 21, 15), reminders.SetCalls[^1].ReminderUtc);
        Assert.Equal(new TimeSpan(18, 15, 0), vm.DetailReminderTime);
    }

    [Fact]
    public async Task A_Reminder_date_and_time_are_saved_as_the_UTC_instant()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.DetailReminderDate = new DateTime(2026, 10, 22);
        s.Vm.DetailReminderTime = new TimeSpan(14, 30, 0);
        await s.Vm.PendingSave;

        Assert.Equal(Utc(22, 17, 30), s.Reminders.SetCalls[^1].ReminderUtc);
        Assert.Equal(Utc(22, 17, 30), s.Tasks.All[0].ReminderUtc);
    }

    [Fact]
    public async Task A_Reminder_time_without_a_date_is_reported_and_not_saved()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.DetailReminderTime = new TimeSpan(14, 30, 0);
        await s.Vm.PendingSave;

        Assert.Empty(s.Reminders.SetCalls);
        Assert.NotNull(s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task A_rejected_Reminder_time_without_a_date_is_shown_under_the_Reminder_field()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.DetailReminderTime = new TimeSpan(14, 30, 0);
        await s.Vm.PendingSave;

        Assert.Equal("A Reminder time needs a Reminder date.", s.Vm.ReminderError);
        Assert.Null(s.Vm.DueError);
        Assert.Null(s.Vm.GeneralError);
    }

    [Fact]
    public async Task Clearing_the_Reminder_time_falls_back_to_the_default_Reminder_time()
    {
        var s = await StartAsync("a");
        await s.Reminders.SetReminderAsync(s.Tasks.All[0].Id, Utc(22, 17, 30), Ct);
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        Assert.True(s.Vm.ClearDetailReminderTimeCommand.CanExecute(null));

        s.Vm.ClearDetailReminderTimeCommand.Execute(null);
        await s.Vm.PendingSave;

        Assert.Equal(Utc(22, 12), s.Reminders.SetCalls[^1].ReminderUtc);
        Assert.Equal(new TimeSpan(9, 0, 0), s.Vm.DetailReminderTime);
        Assert.Equal(new DateTime(2026, 10, 22), s.Vm.DetailReminderDate);
    }

    [Fact]
    public async Task The_clear_Reminder_time_command_needs_a_time()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        Assert.False(s.Vm.ClearDetailReminderTimeCommand.CanExecute(null));

        // A date-only Reminder shows the default time already, so clearing it would change nothing.
        s.Vm.DetailReminderDate = new DateTime(2026, 10, 22);
        await s.Vm.PendingSave;
        Assert.False(s.Vm.ClearDetailReminderTimeCommand.CanExecute(null));

        s.Vm.DetailReminderDate = null;
        await s.Vm.PendingSave;
        Assert.False(s.Vm.ClearDetailReminderTimeCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_clear_Reminder_time_command_is_off_for_a_saved_Reminder_at_the_default_time()
    {
        var s = await StartAsync("a");
        await s.Reminders.SetReminderAsync(s.Tasks.All[0].Id, Utc(22, 12), Ct); // 09:00 local, the default
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        Assert.Equal(new TimeSpan(9, 0, 0), s.Vm.DetailReminderTime);
        Assert.False(s.Vm.ClearDetailReminderTimeCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_clear_Reminder_time_command_turns_off_once_the_time_falls_back_to_the_default()
    {
        var s = await StartAsync("a");
        await s.Reminders.SetReminderAsync(s.Tasks.All[0].Id, Utc(22, 17, 30), Ct);
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.ClearDetailReminderTimeCommand.Execute(null);
        await s.Vm.PendingSave;

        Assert.False(s.Vm.ClearDetailReminderTimeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Picking_a_time_on_a_date_only_Reminder_turns_the_clear_command_on()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        s.Vm.DetailReminderDate = new DateTime(2026, 10, 22);
        await s.Vm.PendingSave;
        var changed = 0;
        s.Vm.ClearDetailReminderTimeCommand.CanExecuteChanged += (_, _) => changed++;

        s.Vm.DetailReminderTime = new TimeSpan(14, 0, 0);
        await s.Vm.PendingSave;

        Assert.True(s.Vm.ClearDetailReminderTimeCommand.CanExecute(null));
        Assert.True(changed > 0, "The clear command did not report its change.");
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

        Assert.Null(s.Vm.DetailReminderTime);
        Assert.Null(s.Reminders.SetCalls[^1].ReminderUtc);
        Assert.Null(s.Tasks.All[0].ReminderUtc);
    }

    [Fact]
    public async Task Editing_a_fired_Reminder_in_the_pane_re_arms_it()
    {
        var s = await StartAsync("a");
        await s.Reminders.SetReminderAsync(s.Tasks.All[0].Id, Utc(20, 12), Ct);
        s.Tasks.Put(FakeTaskService.With(s.Tasks.All[0], t => t.NotifiedThroughUtc = Utc(20, 12)));
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.DetailReminderTime = new TimeSpan(15, 0, 0);
        await s.Vm.PendingSave;

        Assert.Equal(Utc(20, 18), ReminderCalculator.DueInstant(s.Tasks.All[0], new DateTimeOffset(Utc(20, 18))));
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
        Assert.True(s.Vm.IsNarrowed);
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

    [Theory]
    [InlineData(ReminderAction.SnoozeShort, 10)]
    [InlineData(ReminderAction.SnoozeLong, 60)]
    public async Task Snoozing_from_a_notification_snoozes_the_Task_without_showing_the_window(
        ReminderAction action, int minutes)
    {
        var s = await StartAsync("a");
        var id = s.Tasks.All[0].Id;
        await s.Reminders.SetReminderAsync(id, Utc(20, 12), Ct);
        var window = new FakeWindow();

        await new ReminderActivationHandler(s.Vm, window).HandleAsync(new NotificationActivation(id, action), Ct);

        Assert.Equal([(id, TimeSpan.FromMinutes(minutes))], s.Reminders.SnoozeCalls);
        Assert.Equal(Utc(20, 13).AddMinutes(minutes), s.Tasks.All[0].SnoozedUntilUtc);
        Assert.Equal(Utc(20, 12), s.Tasks.All[0].ReminderUtc);
        Assert.Equal(0, window.FocusCount);
        Assert.Null(s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task Snoozing_a_deleted_Task_does_nothing()
    {
        var s = await StartAsync("a");

        await new ReminderActivationHandler(s.Vm, new FakeWindow())
            .HandleAsync(new NotificationActivation(Guid.NewGuid(), ReminderAction.SnoozeShort), Ct);

        Assert.Null(s.Tasks.All[0].SnoozedUntilUtc);
        Assert.Null(s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task Opening_the_missed_Reminders_summary_shows_Today_and_the_window()
    {
        var s = await StartAsync("a");
        var window = new FakeWindow();

        await new ReminderActivationHandler(s.Vm, window).HandleAsync(NotificationActivation.OpenToday, Ct);

        Assert.Equal(SmartView.Today, s.Vm.CurrentView);
        Assert.Equal(1, window.FocusCount);
    }

    [Fact]
    public async Task Opening_a_notification_once_the_exit_started_leaves_the_window_hidden()
    {
        var s = await StartAsync("a");
        var window = new FakeWindow();
        using var exitStarted = new CancellationTokenSource();
        await exitStarted.CancelAsync();
        var handler = new ReminderActivationHandler(s.Vm, window);

        // A cancelled load may throw or return; either way the window stays hidden.
        await Record.ExceptionAsync(() =>
            handler.HandleAsync(new NotificationActivation(s.Tasks.All[0].Id, ReminderAction.Open), exitStarted.Token));
        await Record.ExceptionAsync(() => handler.HandleAsync(NotificationActivation.OpenToday, exitStarted.Token));

        Assert.Equal(0, window.FocusCount);
    }

    // ---- Snooze in the detail pane ----

    [Fact]
    public async Task A_snoozed_Task_shows_its_Snooze_and_clearing_it_re_arms_the_Reminder()
    {
        var s = await StartAsync("a");
        var id = s.Tasks.All[0].Id;
        await s.Reminders.SetReminderAsync(id, Utc(20, 12), Ct);
        await s.Reminders.SnoozeAsync(id, ReminderCalculator.LongSnooze, Ct); // until 14:00 UTC, 11:00 local
        s.Tasks.Put(FakeTaskService.With(s.Tasks.All[0], t => t.NotifiedThroughUtc = Utc(20, 13)));
        await s.Vm.LoadAsync(Ct);

        s.Vm.SelectedTask = s.Vm.Tasks[0];
        Assert.True(s.Vm.HasDetailSnooze);
        Assert.Equal("Snoozed until 11:00", s.Vm.DetailSnooze);

        await s.Vm.ClearSnoozeCommand.ExecuteAsync(null);

        Assert.False(s.Vm.HasDetailSnooze);
        Assert.Null(s.Tasks.All[0].SnoozedUntilUtc);
        Assert.Null(s.Tasks.All[0].NotifiedThroughUtc);
        Assert.Equal(Utc(20, 12), s.Tasks.All[0].ReminderUtc);
    }

    [Fact]
    public async Task Editing_the_Reminder_of_a_snoozed_Task_clears_the_shown_Snooze()
    {
        var s = await StartAsync("a");
        var id = s.Tasks.All[0].Id;
        await s.Reminders.SetReminderAsync(id, Utc(20, 12), Ct);
        await s.Reminders.SnoozeAsync(id, ReminderCalculator.ShortSnooze, Ct);
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.DetailReminderTime = new TimeSpan(15, 0, 0);
        await s.Vm.PendingSave;

        Assert.Null(s.Tasks.All[0].SnoozedUntilUtc);
        Assert.False(s.Vm.HasDetailSnooze);
    }

    [Fact]
    public async Task A_Task_without_a_Snooze_shows_none()
    {
        var s = await StartAsync("a");
        await s.Reminders.SetReminderAsync(s.Tasks.All[0].Id, Utc(20, 12), Ct);
        await s.Vm.LoadAsync(Ct);

        s.Vm.SelectedTask = s.Vm.Tasks[0];

        Assert.False(s.Vm.HasDetailSnooze);
        Assert.Null(s.Vm.DetailSnooze);
    }

    [Fact]
    public async Task Snoozing_the_selected_Task_from_a_notification_shows_the_Snooze_in_the_pane()
    {
        var s = await StartAsync("a");
        var id = s.Tasks.All[0].Id;
        await s.Reminders.SetReminderAsync(id, Utc(20, 12), Ct);
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        await new ReminderActivationHandler(s.Vm, new FakeWindow())
            .HandleAsync(new NotificationActivation(id, ReminderAction.SnoozeShort), Ct);

        Assert.Equal("Snoozed until 10:10", s.Vm.DetailSnooze);
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
