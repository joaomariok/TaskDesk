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

    // Stores the Reminders on the FakeTaskService's Tasks with the Core rules.
    private sealed class FakeTaskReminders(FakeTaskService tasks, TimeProvider clock) : IReminderService
    {
        public List<(Guid TaskId, DateTime? AtUtc)> AddCalls { get; } = [];
        public List<(Guid ReminderId, DateTime? AtUtc)> UpdateCalls { get; } = [];
        public List<Guid> RemoveCalls { get; } = [];
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

        public Task<TaskItem> AddReminderAsync(
            Guid taskId, ReminderValue reminder, CancellationToken cancellationToken = default)
        {
            AddCalls.Add((taskId, reminder.AtUtc));
            var current = tasks.All.SingleOrDefault(t => t.Id == taskId)
                ?? throw new KeyNotFoundException($"Task {taskId} does not exist.");
            var task = FakeTaskService.With(current, t => ReminderCalculator.AddReminder(t, reminder, clock.GetUtcNow()));
            tasks.Put(task);
            return Task.FromResult(task);
        }

        public Task<TaskItem> UpdateReminderAsync(
            Guid reminderId, ReminderValue reminder, CancellationToken cancellationToken = default)
        {
            UpdateCalls.Add((reminderId, reminder.AtUtc));
            var task = FakeTaskService.With(
                Owner(reminderId), t => ReminderCalculator.UpdateReminder(t, reminderId, reminder, clock.GetUtcNow()));
            tasks.Put(task);
            return Task.FromResult(task);
        }

        public Task<TaskItem> RemoveReminderAsync(Guid reminderId, CancellationToken cancellationToken = default)
        {
            RemoveCalls.Add(reminderId);
            var task = FakeTaskService.With(
                Owner(reminderId), t => ReminderCalculator.RemoveReminder(t, reminderId, clock.GetUtcNow()));
            tasks.Put(task);
            return Task.FromResult(task);
        }

        private TaskItem Owner(Guid reminderId) =>
            tasks.All.SingleOrDefault(t => t.Reminders.Any(r => r.Id == reminderId))
            ?? throw new KeyNotFoundException($"Reminder {reminderId} does not exist.");

        public Task<IReadOnlyList<DueReminder>> ListDueAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaskItem> SetNotifyAtDueAsync(
            Guid taskId, bool notifyAtDue, CancellationToken cancellationToken = default) =>
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
                [.. tasks.All.Where(t => !query.HasReminder || t.Reminders.Count > 0)]);
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

    // Adds Reminders to the first Task, reloads, opens it in the pane and forgets the calls the arrangement made.
    private static async Task SelectWithRemindersAsync(Setup s, params DateTime[] reminders)
    {
        foreach (var at in reminders)
        {
            await s.Reminders.AddReminderAsync(s.Tasks.All[0].Id, ReminderValue.Fixed(at), Ct);
        }

        s.Reminders.AddCalls.Clear();
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];
    }

    private static ReminderEditorViewModel Edit(Setup s, int row)
    {
        s.Vm.DetailReminders[row].EditCommand.Execute(null);
        return s.Vm.ReminderEditor;
    }

    private static ReminderEditorViewModel Add(Setup s)
    {
        s.Vm.AddReminderCommand.Execute(null);
        return s.Vm.ReminderEditor;
    }

    private static async Task SaveAsync(Setup s)
    {
        s.Vm.ReminderEditor.SaveCommand.Execute(null);
        await s.Vm.PendingSave;
    }

    // ---- Detail pane: the Reminder rows ----

    [Fact]
    public async Task Selecting_a_Task_lists_its_Reminders_as_local_date_and_time_the_first_one_first()
    {
        var s = await StartAsync("a");

        await SelectWithRemindersAsync(s, Utc(21, 1, 15), Utc(20, 17));

        Assert.Equal(["Today 14:00", "Today 22:15"], s.Vm.DetailReminders.Select(r => r.Text));
        Assert.Equal([Utc(20, 17), Utc(21, 1, 15)], s.Vm.DetailReminders.Select(r => r.FiresAtUtc));
        var editor = Edit(s, 1);
        Assert.Equal(new DateTime(2026, 10, 20), editor.Date);
        Assert.Equal(new TimeSpan(22, 15, 0), editor.Time);
    }

    [Fact]
    public async Task A_Task_without_a_Reminder_shows_no_rows_and_Add_opens_an_empty_editor()
    {
        var s = await StartAsync("a");

        s.Vm.SelectedTask = s.Vm.Tasks[0];

        Assert.Empty(s.Vm.DetailReminders);
        var editor = Add(s);
        Assert.True(editor.IsOpen);
        Assert.False(editor.IsEditing);
        Assert.Null(editor.Date);
        Assert.Null(editor.Time);
    }

    [Fact]
    public async Task A_past_Reminder_stays_listed_and_is_shown_and_announced_as_past()
    {
        var s = await StartAsync("a");

        await SelectWithRemindersAsync(s, Utc(20, 12), Utc(20, 14));

        var past = s.Vm.DetailReminders[0];
        var ahead = s.Vm.DetailReminders[1];
        Assert.True(past.IsPast);
        Assert.False(ahead.IsPast);
        Assert.Equal("Reminder Today 09:00, past", past.AccessibleName);
        Assert.Equal("Reminder Today 11:00", ahead.AccessibleName);
        Assert.Equal("Remove reminder Today 09:00", past.RemoveName);
    }

    [Fact]
    public async Task Moving_to_another_Task_shows_its_Reminders()
    {
        var s = await StartAsync("a", "b");
        await SelectWithRemindersAsync(s, Utc(20, 14));
        Assert.Single(s.Vm.DetailReminders);

        s.Vm.SelectedTask = s.Vm.Tasks[1];

        Assert.Empty(s.Vm.DetailReminders);
    }

    [Fact]
    public async Task There_is_no_limit_on_the_number_of_Reminders()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        for (var day = 21; day <= 31; day++)
        {
            foreach (var hour in new[] { 8, 12, 18 })
            {
                var editor = Add(s);
                editor.Date = new DateTime(2026, 10, day);
                editor.Time = new TimeSpan(hour, 0, 0);
                await SaveAsync(s);
            }
        }

        Assert.Equal(33, s.Vm.DetailReminders.Count);
        Assert.Equal(33, s.Tasks.All[0].Reminders.Count);
        Assert.Null(s.Vm.ErrorMessage);
    }

    // ---- Detail pane: the flyout editor ----

    [Fact]
    public async Task A_date_only_Reminder_is_saved_at_09_00_local_and_shows_that_time_without_a_Due_date()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        var editor = Add(s);
        editor.Date = new DateTime(2026, 10, 22);
        await SaveAsync(s);

        Assert.Equal([(s.Tasks.All[0].Id, (DateTime?)Utc(22, 12))], s.Reminders.AddCalls);
        Assert.EndsWith(" 09:00", Assert.Single(s.Vm.DetailReminders).Text);
        Assert.False(editor.IsOpen);
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
        vm.AddReminderCommand.Execute(null);
        vm.ReminderEditor.Date = new DateTime(2026, 10, 22);
        vm.ReminderEditor.SaveCommand.Execute(null);
        await vm.PendingSave;

        Assert.Equal(Utc(22, 10, 30), reminders.AddCalls[^1].AtUtc);
        Assert.EndsWith(" 07:30", Assert.Single(vm.DetailReminders).Text);

        // Changed in Settings while the app runs: the next date-only Reminder uses it, no restart needed.
        settings.Current = settings.Current with { DefaultReminderTime = new TimeOnly(18, 15) };
        vm.SelectedTask = vm.Tasks[1];
        vm.AddReminderCommand.Execute(null);
        vm.ReminderEditor.Date = new DateTime(2026, 10, 23);
        vm.ReminderEditor.SaveCommand.Execute(null);
        await vm.PendingSave;

        Assert.Equal(Utc(23, 21, 15), reminders.AddCalls[^1].AtUtc);
        Assert.EndsWith(" 18:15", Assert.Single(vm.DetailReminders).Text);
    }

    [Fact]
    public async Task A_Reminder_date_and_time_are_saved_as_the_UTC_instant()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        var editor = Add(s);
        editor.Date = new DateTime(2026, 10, 22);
        editor.Time = new TimeSpan(14, 30, 0);
        await SaveAsync(s);

        Assert.Equal(Utc(22, 17, 30), s.Reminders.AddCalls[^1].AtUtc);
        Assert.Equal(Utc(22, 17, 30), Assert.Single(s.Tasks.All[0].Reminders).AtUtc);
    }

    [Fact]
    public async Task A_Reminder_time_without_a_date_is_reported_and_not_saved()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        var editor = Add(s);
        editor.Time = new TimeSpan(14, 30, 0);
        await SaveAsync(s);

        Assert.Empty(s.Reminders.AddCalls);
        Assert.NotNull(s.Vm.ErrorMessage);
        Assert.True(editor.IsOpen);
    }

    [Fact]
    public async Task A_rejected_Reminder_time_without_a_date_is_shown_under_the_Reminder_field()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        Add(s).Time = new TimeSpan(14, 30, 0);
        await SaveAsync(s);

        Assert.Equal("A Reminder time needs a Reminder date.", s.Vm.ReminderError);
        Assert.Null(s.Vm.DueError);
        Assert.Null(s.Vm.GeneralError);
    }

    [Fact]
    public async Task Saving_without_a_date_is_reported_under_Reminders_and_not_saved()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        Add(s);
        await SaveAsync(s);

        Assert.Empty(s.Reminders.AddCalls);
        Assert.Equal("A Reminder needs a date.", s.Vm.ReminderError);
    }

    [Fact]
    public async Task A_duplicate_Reminder_is_refused_with_an_error_under_Reminders()
    {
        var s = await StartAsync("a");
        await SelectWithRemindersAsync(s, Utc(22, 17, 30));

        var editor = Add(s);
        editor.Date = new DateTime(2026, 10, 22);
        editor.Time = new TimeSpan(14, 30, 0);
        await SaveAsync(s);

        Assert.Equal("The Task already has this Reminder.", s.Vm.ReminderError);
        Assert.Null(s.Vm.GeneralError);
        Assert.Single(s.Vm.DetailReminders);
        Assert.Single(s.Tasks.All[0].Reminders);
    }

    [Fact]
    public async Task A_later_save_clears_the_Reminder_error()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        Add(s);
        await SaveAsync(s);
        Assert.NotNull(s.Vm.ReminderError);

        s.Vm.ReminderEditor.Date = new DateTime(2026, 10, 22);
        await SaveAsync(s);

        Assert.Null(s.Vm.ReminderError);
        Assert.Null(s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task Cancel_closes_the_editor_without_saving()
    {
        var s = await StartAsync("a");
        await SelectWithRemindersAsync(s, Utc(22, 17, 30));

        var editor = Edit(s, 0);
        editor.Time = new TimeSpan(8, 0, 0);
        editor.CancelCommand.Execute(null);
        await s.Vm.PendingSave;

        Assert.False(editor.IsOpen);
        Assert.Empty(s.Reminders.UpdateCalls);
        Assert.Equal(Utc(22, 17, 30), Assert.Single(s.Tasks.All[0].Reminders).AtUtc);

        // Opening it again shows the Reminder as saved, not the cancelled edit.
        Assert.Equal(new TimeSpan(14, 30, 0), Edit(s, 0).Time);
    }

    [Fact]
    public async Task A_click_on_a_row_opens_the_editor_on_that_Reminder_and_Save_changes_it()
    {
        var s = await StartAsync("a");
        await SelectWithRemindersAsync(s, Utc(22, 17, 30), Utc(23, 12));
        var id = s.Vm.DetailReminders[1].Id;

        var editor = Edit(s, 1);
        Assert.True(editor.IsEditing);
        editor.Date = new DateTime(2026, 10, 21);
        await SaveAsync(s);

        Assert.Equal([(id, (DateTime?)Utc(21, 12))], s.Reminders.UpdateCalls);
        Assert.Empty(s.Reminders.AddCalls);
        Assert.Equal([Utc(21, 12), Utc(22, 17, 30)], s.Vm.DetailReminders.Select(r => r.FiresAtUtc));
    }

    [Fact]
    public async Task Add_after_an_edit_opens_an_empty_editor()
    {
        var s = await StartAsync("a");
        await SelectWithRemindersAsync(s, Utc(22, 17, 30));
        Edit(s, 0).CancelCommand.Execute(null);

        var editor = Add(s);

        Assert.False(editor.IsEditing);
        Assert.Null(editor.Date);
        Assert.Null(editor.Time);
    }

    [Fact]
    public async Task Clearing_the_Reminder_time_falls_back_to_the_default_Reminder_time()
    {
        var s = await StartAsync("a");
        await SelectWithRemindersAsync(s, Utc(22, 17, 30));
        var editor = Edit(s, 0);
        Assert.True(editor.ClearTimeCommand.CanExecute(null));

        editor.ClearTimeCommand.Execute(null);
        Assert.Equal(new DateTime(2026, 10, 22), editor.Date);
        await SaveAsync(s);

        Assert.Equal(Utc(22, 12), s.Reminders.UpdateCalls[^1].AtUtc);
        Assert.EndsWith(" 09:00", Assert.Single(s.Vm.DetailReminders).Text);
    }

    [Fact]
    public async Task The_clear_Reminder_time_command_needs_a_time()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        var editor = Add(s);
        Assert.False(editor.ClearTimeCommand.CanExecute(null));

        // A date-only Reminder fires at the default time already, so clearing would change nothing.
        editor.Date = new DateTime(2026, 10, 22);
        Assert.False(editor.ClearTimeCommand.CanExecute(null));

        editor.Date = null;
        Assert.False(editor.ClearTimeCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_clear_Reminder_time_command_is_off_for_a_saved_Reminder_at_the_default_time()
    {
        var s = await StartAsync("a");
        await SelectWithRemindersAsync(s, Utc(22, 12)); // 09:00 local, the default

        var editor = Edit(s, 0);

        Assert.Equal(new TimeSpan(9, 0, 0), editor.Time);
        Assert.False(editor.ClearTimeCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_clear_Reminder_time_command_turns_off_once_the_time_falls_back_to_the_default()
    {
        var s = await StartAsync("a");
        await SelectWithRemindersAsync(s, Utc(22, 17, 30));
        var editor = Edit(s, 0);

        editor.ClearTimeCommand.Execute(null);

        Assert.False(editor.ClearTimeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Picking_a_time_on_a_date_only_Reminder_turns_the_clear_command_on()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        var editor = Add(s);
        editor.Date = new DateTime(2026, 10, 22);
        var changed = 0;
        editor.ClearTimeCommand.CanExecuteChanged += (_, _) => changed++;

        editor.Time = new TimeSpan(14, 0, 0);

        Assert.True(editor.ClearTimeCommand.CanExecute(null));
        Assert.True(changed > 0, "The clear command did not report its change.");
    }

    [Fact]
    public async Task The_editor_time_applies_once_a_date_is_set()
    {
        var s = await StartAsync("a");
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        var editor = Add(s);
        Assert.False(editor.HasDate);

        editor.Date = new DateTime(2026, 10, 22);

        Assert.True(editor.HasDate);
    }

    [Fact]
    public async Task Clearing_the_editor_date_clears_its_time()
    {
        var s = await StartAsync("a");
        await SelectWithRemindersAsync(s, Utc(22, 17, 30));
        var editor = Edit(s, 0);

        editor.Date = null;

        Assert.Null(editor.Time);
    }

    [Fact]
    public async Task The_remove_button_removes_that_Reminder()
    {
        var s = await StartAsync("a");
        await SelectWithRemindersAsync(s, Utc(22, 17, 30), Utc(23, 12));
        var first = s.Vm.DetailReminders[0].Id;

        await s.Vm.DetailReminders[0].RemoveCommand.ExecuteAsync(null);
        await s.Vm.PendingSave;

        Assert.Equal([first], s.Reminders.RemoveCalls);
        Assert.Equal([Utc(23, 12)], s.Vm.DetailReminders.Select(r => r.FiresAtUtc));
        Assert.Equal([(DateTime?)Utc(23, 12)], s.Tasks.All[0].Reminders.Select(r => r.AtUtc));
    }

    [Fact]
    public async Task Editing_a_fired_Reminder_in_the_pane_re_arms_it()
    {
        var s = await StartAsync("a");
        await s.Reminders.AddReminderAsync(s.Tasks.All[0].Id, ReminderValue.Fixed(Utc(20, 12)), Ct);
        s.Tasks.Put(FakeTaskService.With(s.Tasks.All[0], t => t.NotifiedThroughUtc = Utc(20, 12)));
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        Edit(s, 0).Time = new TimeSpan(15, 0, 0);
        await SaveAsync(s);

        Assert.Equal(Utc(20, 18), ReminderCalculator.DueInstant(s.Tasks.All[0], new DateTimeOffset(Utc(20, 18))));
        Assert.Equal(Utc(20, 18), Assert.Single(s.Tasks.All[0].Reminders).AtUtc);
    }

    // ---- Filter chip ----

    [Fact]
    public async Task The_has_reminder_chip_filters_the_view_and_clear_all_resets_it()
    {
        var s = await StartAsync("plain", "reminded");
        await s.Reminders.AddReminderAsync(s.Tasks.All[1].Id, ReminderValue.Fixed(Utc(22, 12)), Ct);

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
        await s.Reminders.AddReminderAsync(id, ReminderValue.Fixed(Utc(20, 12)), Ct);
        var window = new FakeWindow();

        await new ReminderActivationHandler(s.Vm, window).HandleAsync(new NotificationActivation(id, action), Ct);

        Assert.Equal([(id, TimeSpan.FromMinutes(minutes))], s.Reminders.SnoozeCalls);
        Assert.Equal(Utc(20, 13).AddMinutes(minutes), s.Tasks.All[0].SnoozedUntilUtc);
        Assert.Equal(Utc(20, 12), Assert.Single(s.Tasks.All[0].Reminders).AtUtc);
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
        await s.Reminders.AddReminderAsync(id, ReminderValue.Fixed(Utc(20, 12)), Ct);
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
        Assert.Equal(Utc(20, 12), Assert.Single(s.Tasks.All[0].Reminders).AtUtc);
    }

    [Fact]
    public async Task Editing_the_Reminder_of_a_snoozed_Task_clears_the_shown_Snooze()
    {
        var s = await StartAsync("a");
        var id = s.Tasks.All[0].Id;
        await s.Reminders.AddReminderAsync(id, ReminderValue.Fixed(Utc(20, 12)), Ct);
        await s.Reminders.SnoozeAsync(id, ReminderCalculator.ShortSnooze, Ct);
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        Edit(s, 0).Time = new TimeSpan(15, 0, 0);
        s.Vm.ReminderEditor.SaveCommand.Execute(null);
        await s.Vm.PendingSave;

        Assert.Null(s.Tasks.All[0].SnoozedUntilUtc);
        Assert.False(s.Vm.HasDetailSnooze);
    }

    [Fact]
    public async Task A_Task_without_a_Snooze_shows_none()
    {
        var s = await StartAsync("a");
        await s.Reminders.AddReminderAsync(s.Tasks.All[0].Id, ReminderValue.Fixed(Utc(20, 12)), Ct);
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
        await s.Reminders.AddReminderAsync(id, ReminderValue.Fixed(Utc(20, 12)), Ct);
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
