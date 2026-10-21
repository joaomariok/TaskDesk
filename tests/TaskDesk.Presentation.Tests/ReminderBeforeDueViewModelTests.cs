using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tasks;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

public class ReminderBeforeDueViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Local time is UTC-3; the clock is at local 2026-10-20 10:00, a Tuesday.
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test-3", TimeSpan.FromHours(-3), "Test-3", "Test-3");

    private static readonly DateOnly Thursday = new(2026, 10, 22);

    private static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    private sealed class FakeSettingsStore : IShellSettingsStore
    {
        public ShellSettings Current { get; set; } = new();
        public ShellSettings Load() => Current;
        public void Save(ShellSettings settings) => Current = settings;
    }

    private static MomentRules Rules(FakeTimeProvider clock, FakeSettingsStore settings) =>
        new(clock.LocalTimeZone, settings.Current.DefaultReminderTime);

    // Stores the Reminders and the Due on the FakeTaskService's Tasks with the Core rules.
    private sealed class FakeServices(FakeTaskService tasks, FakeTimeProvider clock, FakeSettingsStore settings)
        : IReminderService, IDueService
    {
        public List<ReminderValue> Added { get; } = [];
        public List<(Guid ReminderId, ReminderValue Value)> Updated { get; } = [];

        public Task<TaskItem> AddReminderAsync(
            Guid taskId, ReminderValue reminder, CancellationToken cancellationToken = default)
        {
            Added.Add(reminder);
            return Change(
                tasks.All.Single(t => t.Id == taskId),
                t => ReminderCalculator.AddReminder(t, reminder, clock.GetUtcNow(), Rules(clock, settings)));
        }

        public Task<TaskItem> UpdateReminderAsync(
            Guid reminderId, ReminderValue reminder, CancellationToken cancellationToken = default)
        {
            Updated.Add((reminderId, reminder));
            return Change(
                Owner(reminderId),
                t => ReminderCalculator.UpdateReminder(t, reminderId, reminder, clock.GetUtcNow(), Rules(clock, settings)));
        }

        public Task<TaskItem> RemoveReminderAsync(Guid reminderId, CancellationToken cancellationToken = default) =>
            Change(Owner(reminderId), t => ReminderCalculator.RemoveReminder(t, reminderId, clock.GetUtcNow()));

        public Task<TaskItem> SetDueAsync(
            Guid taskId, DateOnly? dueDate, TimeOnly? dueTime, CancellationToken cancellationToken = default) =>
            Change(
                tasks.All.Single(t => t.Id == taskId),
                t => ReminderCalculator.ChangeDue(t, dueDate, dueTime, clock.GetUtcNow()));

        private Task<TaskItem> Change(TaskItem current, Action<TaskItem> change)
        {
            var task = FakeTaskService.With(current, change);
            tasks.Put(task);
            return Task.FromResult(task);
        }

        private TaskItem Owner(Guid reminderId) => tasks.All.Single(t => t.Reminders.Any(r => r.Id == reminderId));

        public Task<SmartViewCounts> CountSmartViewsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SmartViewCounts(0, 0, 0, tasks.All.Count));

        public Task<IReadOnlyList<TaskItem>> ListSmartViewAsync(
            SmartView view, int skip = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>([.. tasks.All.Where(t => !t.IsCompleted)]);

        public Task<TaskItem> SetNotifyAtDueAsync(
            Guid taskId, bool notifyAtDue, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaskItem> SetNagPeriodAsync(
            Guid taskId, NagPeriod? period, CancellationToken cancellationToken = default) =>
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

    private sealed record Setup(FakeTaskService Tasks, FakeServices Services, MainViewModel Vm, FakeSettingsStore Settings);

    // One Task "a", due on Thursday (date-only) unless told otherwise, selected in the pane.
    private static async Task<Setup> StartAsync(DateOnly? due = null, TimeOnly? time = null, bool withDue = true)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(Zone);
        var tasks = new FakeTaskService();
        var created = await tasks.CreateTaskAsync("a", Ct);
        var settings = new FakeSettingsStore();
        var services = new FakeServices(tasks, clock, settings);
        if (withDue)
        {
            await services.SetDueAsync(created.Id, due ?? Thursday, time, Ct);
        }

        var vm = new MainViewModel(
            tasks, new FakeTagService(tasks), clock, services, new FakeSearch(tasks), reminderService: services,
            settingsStore: settings);
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks[0];
        return new Setup(tasks, services, vm, settings);
    }

    private static ReminderEditorViewModel AddBeforeDue(Setup s)
    {
        s.Vm.AddReminderCommand.Execute(null);
        var editor = s.Vm.ReminderEditor;
        editor.ModeIndex = ReminderEditorViewModel.BeforeDueMode;
        return editor;
    }

    private static async Task SaveAsync(Setup s)
    {
        s.Vm.ReminderEditor.SaveCommand.Execute(null);
        await s.Vm.PendingSave;
    }

    private static int PresetIndex(string label) =>
        ReminderEditorViewModel.OffsetChoices.ToList().IndexOf(label);

    [Fact]
    public void The_presets_are_5_15_30_min_1_2_h_1_2_days_1_week_and_Custom()
    {
        Assert.Equal(
            ["5 min", "15 min", "30 min", "1 h", "2 h", "1 day", "2 days", "1 week", "Custom…"],
            ReminderEditorViewModel.OffsetChoices);
        Assert.Equal(["minutes", "hours", "days", "weeks"], ReminderEditorViewModel.UnitChoices);
    }

    public static TheoryData<string, int, ReminderUnit> PresetCases => new()
    {
        { "5 min", 5, ReminderUnit.Minutes },
        { "15 min", 15, ReminderUnit.Minutes },
        { "30 min", 30, ReminderUnit.Minutes },
        { "1 h", 1, ReminderUnit.Hours },
        { "2 h", 2, ReminderUnit.Hours },
        { "1 day", 1, ReminderUnit.Days },
        { "2 days", 2, ReminderUnit.Days },
        { "1 week", 1, ReminderUnit.Weeks },
    };

    [Theory]
    [MemberData(nameof(PresetCases))]
    public async Task A_preset_saves_its_amount_and_unit(string preset, int amount, ReminderUnit unit)
    {
        var s = await StartAsync();
        var editor = AddBeforeDue(s);

        editor.OffsetIndex = PresetIndex(preset);
        await SaveAsync(s);

        Assert.Equal([ReminderValue.BeforeDue(amount, unit)], s.Services.Added);
        Assert.False(editor.IsOpen);
        Assert.Null(s.Vm.ReminderError);
    }

    [Fact]
    public async Task Custom_takes_a_whole_number_of_a_unit()
    {
        var s = await StartAsync();
        var editor = AddBeforeDue(s);
        Assert.False(editor.IsCustom);

        editor.OffsetIndex = PresetIndex("Custom…");
        editor.CustomAmount = 3;
        editor.CustomUnitIndex = 2;
        await SaveAsync(s);

        Assert.True(editor.IsCustom);
        Assert.Equal([ReminderValue.BeforeDue(3, ReminderUnit.Days)], s.Services.Added);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(2.5)]
    [InlineData(double.NaN)]
    public async Task A_custom_amount_outside_1_to_999_is_refused_under_Reminders(double amount)
    {
        var s = await StartAsync();
        var editor = AddBeforeDue(s);

        editor.OffsetIndex = PresetIndex("Custom…");
        editor.CustomAmount = amount;
        await SaveAsync(s);

        Assert.Empty(s.Services.Added);
        Assert.True(editor.IsOpen);
        Assert.Equal("A Reminder before Due needs a whole number from 1 to 999.", s.Vm.ReminderError);
    }

    [Fact]
    public async Task Before_Due_is_unavailable_while_the_Task_has_no_Due()
    {
        var s = await StartAsync(withDue: false);

        s.Vm.AddReminderCommand.Execute(null);
        var editor = s.Vm.ReminderEditor;

        Assert.False(editor.CanUseBeforeDue);
        Assert.Equal(ReminderEditorViewModel.DateAndTimeMode, editor.ModeIndex);

        editor.ModeIndex = ReminderEditorViewModel.BeforeDueMode;
        await SaveAsync(s);
        Assert.Empty(s.Services.Added);
        Assert.Equal("A Reminder before Due needs a Due.", s.Vm.ReminderError);
    }

    [Fact]
    public async Task Before_Due_becomes_available_once_the_Task_has_a_Due()
    {
        var s = await StartAsync(withDue: false);
        s.Vm.DetailDueDate = new DateTime(2026, 10, 22);
        await s.Vm.PendingSave;

        s.Vm.AddReminderCommand.Execute(null);

        Assert.True(s.Vm.ReminderEditor.CanUseBeforeDue);
    }

    [Fact]
    public async Task A_before_Due_row_reads_its_offset_and_the_moment_it_fires()
    {
        // Due Thursday, date-only, default 09:00: "1 day before" fires Wednesday 09:00.
        var s = await StartAsync();
        var editor = AddBeforeDue(s);
        editor.OffsetIndex = PresetIndex("1 day");
        await SaveAsync(s);

        var row = Assert.Single(s.Vm.DetailReminders);
        Assert.Equal("1 day before Due · Tomorrow 09:00", row.Text);
        Assert.Equal(Utc(21, 12), row.FiresAtUtc);
        Assert.False(row.IsPast);
    }

    public static TheoryData<int, ReminderUnit, string> OffsetTextCases => new()
    {
        { 1, ReminderUnit.Minutes, "1 minute" },
        { 5, ReminderUnit.Minutes, "5 minutes" },
        { 1, ReminderUnit.Hours, "1 hour" },
        { 36, ReminderUnit.Hours, "36 hours" },
        { 2, ReminderUnit.Days, "2 days" },
        { 1, ReminderUnit.Weeks, "1 week" },
        { 3, ReminderUnit.Weeks, "3 weeks" },
    };

    [Theory]
    [MemberData(nameof(OffsetTextCases))]
    public async Task A_before_Due_row_names_its_amount_and_unit(int amount, ReminderUnit unit, string text)
    {
        var s = await StartAsync(due: new DateOnly(2026, 12, 31));

        await s.Services.AddReminderAsync(s.Tasks.All[0].Id, ReminderValue.BeforeDue(amount, unit), Ct);
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        Assert.StartsWith(text + " before Due · ", Assert.Single(s.Vm.DetailReminders).Text);
    }

    [Fact]
    public async Task Rows_are_ordered_with_the_fixed_Reminders_by_the_moment_they_fire()
    {
        // Due Thursday 14:00 local (17:00 UTC).
        var s = await StartAsync(time: new TimeOnly(14, 0));
        var id = s.Tasks.All[0].Id;
        await s.Services.AddReminderAsync(id, ReminderValue.BeforeDue(1, ReminderUnit.Hours), Ct);
        await s.Services.AddReminderAsync(id, ReminderValue.Fixed(Utc(22, 15)), Ct);
        await s.Services.AddReminderAsync(id, ReminderValue.BeforeDue(1, ReminderUnit.Days), Ct);
        await s.Vm.LoadAsync(Ct);

        s.Vm.SelectedTask = s.Vm.Tasks[0];

        Assert.Equal([Utc(21, 17), Utc(22, 15), Utc(22, 16)], s.Vm.DetailReminders.Select(r => r.FiresAtUtc));
        Assert.StartsWith("1 day before Due", s.Vm.DetailReminders[0].Text);
        Assert.StartsWith("1 hour before Due", s.Vm.DetailReminders[2].Text);
    }

    [Fact]
    public async Task Rows_refresh_after_a_Due_change_and_clearing_the_Due_removes_before_Due_rows()
    {
        var s = await StartAsync();
        var id = s.Tasks.All[0].Id;
        await s.Services.AddReminderAsync(id, ReminderValue.BeforeDue(2, ReminderUnit.Hours), Ct);
        await s.Services.AddReminderAsync(id, ReminderValue.Fixed(Utc(25, 12)), Ct);
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        Assert.Equal([Utc(22, 10), Utc(25, 12)], s.Vm.DetailReminders.Select(r => r.FiresAtUtc));

        s.Vm.DetailDueDate = new DateTime(2026, 10, 23);
        await s.Vm.PendingSave;

        Assert.Equal([Utc(23, 10), Utc(25, 12)], s.Vm.DetailReminders.Select(r => r.FiresAtUtc));
        var friday = new DateOnly(2026, 10, 23).ToString("ddd", System.Globalization.CultureInfo.CurrentCulture);
        Assert.Equal($"2 hours before Due · {friday} 07:00", s.Vm.DetailReminders[0].Text);

        s.Vm.DetailDueDate = null;
        await s.Vm.PendingSave;

        Assert.Equal([Utc(25, 12)], s.Vm.DetailReminders.Select(r => r.FiresAtUtc));
    }

    [Fact]
    public async Task Rows_follow_the_default_Reminder_time_setting()
    {
        var s = await StartAsync();
        await s.Services.AddReminderAsync(s.Tasks.All[0].Id, ReminderValue.BeforeDue(1, ReminderUnit.Days), Ct);
        s.Settings.Current = s.Settings.Current with { DefaultReminderTime = new TimeOnly(7, 30) };
        await s.Vm.LoadAsync(Ct);

        s.Vm.SelectedTask = s.Vm.Tasks[0];

        Assert.Equal(Utc(21, 10, 30), Assert.Single(s.Vm.DetailReminders).FiresAtUtc);
    }

    [Fact]
    public async Task A_click_on_a_before_Due_row_opens_the_editor_on_it_and_Save_changes_it()
    {
        var s = await StartAsync();
        var id = s.Tasks.All[0].Id;
        await s.Services.AddReminderAsync(id, ReminderValue.BeforeDue(2, ReminderUnit.Days), Ct);
        await s.Services.AddReminderAsync(id, ReminderValue.BeforeDue(3, ReminderUnit.Days), Ct);
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.DetailReminders[1].EditCommand.Execute(null);
        var preset = s.Vm.ReminderEditor;
        Assert.True(preset.IsEditing);
        Assert.Equal(ReminderEditorViewModel.BeforeDueMode, preset.ModeIndex);
        Assert.Equal(PresetIndex("2 days"), preset.OffsetIndex);

        s.Vm.DetailReminders[0].EditCommand.Execute(null);
        var custom = s.Vm.ReminderEditor;
        Assert.True(custom.IsCustom);
        Assert.Equal(3, custom.CustomAmount);
        Assert.Equal(2, custom.CustomUnitIndex);

        var edited = s.Vm.DetailReminders[0].Id;
        custom.CustomAmount = 4;
        await SaveAsync(s);

        Assert.Equal([(edited, ReminderValue.BeforeDue(4, ReminderUnit.Days))], s.Services.Updated);
        Assert.Equal(
            [ReminderValue.BeforeDue(4, ReminderUnit.Days), ReminderValue.BeforeDue(2, ReminderUnit.Days)],
            s.Tasks.All[0].Reminders.Select(r => r.Value));
        Assert.StartsWith("4 days before Due", s.Vm.DetailReminders[0].Text);
    }

    [Fact]
    public async Task A_duplicate_before_Due_Reminder_is_refused_with_an_error_under_Reminders()
    {
        var s = await StartAsync();
        await s.Services.AddReminderAsync(s.Tasks.All[0].Id, ReminderValue.BeforeDue(1, ReminderUnit.Days), Ct);
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        var editor = AddBeforeDue(s);
        editor.OffsetIndex = PresetIndex("1 day");
        await SaveAsync(s);

        Assert.Equal("The Task already has this Reminder.", s.Vm.ReminderError);
        Assert.Single(s.Vm.DetailReminders);
    }

    [Fact]
    public async Task Add_after_editing_a_before_Due_row_opens_an_empty_Date_and_time_editor()
    {
        var s = await StartAsync();
        await s.Services.AddReminderAsync(s.Tasks.All[0].Id, ReminderValue.BeforeDue(1, ReminderUnit.Weeks), Ct);
        await s.Vm.LoadAsync(Ct);
        s.Vm.SelectedTask = s.Vm.Tasks[0];
        s.Vm.DetailReminders[0].EditCommand.Execute(null);

        s.Vm.AddReminderCommand.Execute(null);

        var editor = s.Vm.ReminderEditor;
        Assert.False(editor.IsEditing);
        Assert.Equal(ReminderEditorViewModel.DateAndTimeMode, editor.ModeIndex);
        Assert.False(editor.IsBeforeDue);
        Assert.Equal(0, editor.OffsetIndex);
        Assert.Null(editor.Date);
    }
}
