using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

public class RecurrenceViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // 2026-10-20 is a Tuesday.
    private static DateOnly Day(int day) => new(2026, 10, day);

    private sealed class FakeDialogs : IOccurrenceDialogs
    {
        public OccurrenceDeleteChoice Answer { get; set; } = OccurrenceDeleteChoice.Cancel;
        public List<string> Asked { get; } = [];

        public Task<OccurrenceDeleteChoice> AskDeleteOccurrenceAsync(string taskTitle)
        {
            Asked.Add(taskTitle);
            return Task.FromResult(Answer);
        }
    }

    // Mutates the Tasks held by the FakeTaskService; the real rules are tested against SQLite.
    private sealed class FakeRecurrenceService(FakeTaskService tasks) : IRecurrenceService
    {
        public List<(Guid TaskId, Recurrence? Recurrence)> SetCalls { get; } = [];
        public List<(Guid TaskId, Guid? Produced)> UndoCompletionCalls { get; } = [];
        public List<Guid> Skipped { get; } = [];
        public List<SkippedOccurrence> UndoneSkips { get; } = [];

        private TaskItem Find(Guid id) => tasks.All.Single(t => t.Id == id);

        public Task<TaskItem> SetRecurrenceAsync(
            Guid taskId, Recurrence? recurrence, CancellationToken cancellationToken = default)
        {
            recurrence?.Validate();
            SetCalls.Add((taskId, recurrence));
            var updated = FakeTaskService.With(Find(taskId), t => t.Recurrence = recurrence);
            tasks.Put(updated);
            return Task.FromResult(updated);
        }

        public Task<TaskItem> UndoCompletionAsync(
            Guid taskId, Guid? producedOccurrenceId, CancellationToken cancellationToken = default)
        {
            UndoCompletionCalls.Add((taskId, producedOccurrenceId));
            var updated = FakeTaskService.With(Find(taskId), t =>
            {
                t.IsCompleted = false;
                t.CompletedUtc = null;
                if (producedOccurrenceId is not null && t.NextOccurrenceId == producedOccurrenceId)
                {
                    t.NextOccurrenceId = null;
                }
            });
            if (producedOccurrenceId is { } produced)
            {
                tasks.Remove(produced);
            }

            tasks.Put(updated);
            return Task.FromResult(updated);
        }

        public Task<SkippedOccurrence> SkipOccurrenceAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            Skipped.Add(taskId);
            var task = Find(taskId);
            Guid? produced = null;
            if (task.NextOccurrenceId is null)
            {
                var next = FakeTaskService.With(task, t =>
                {
                    t.Id = Guid.NewGuid();
                    t.DueDate = task.DueDate?.AddDays(1);
                    t.SortOrder = task.SortOrder + 0.5;
                });
                tasks.Put(next);
                produced = next.Id;
                task = FakeTaskService.With(task, t => t.NextOccurrenceId = next.Id);
            }

            tasks.Remove(taskId);
            return Task.FromResult(new SkippedOccurrence(new DeletedTask(task, [], []), produced));
        }

        public Task<TaskItem> UndoSkipAsync(SkippedOccurrence skipped, CancellationToken cancellationToken = default)
        {
            UndoneSkips.Add(skipped);
            var restored = FakeTaskService.With(skipped.Deleted.Task, t =>
            {
                if (skipped.ProducedOccurrenceId is not null)
                {
                    t.NextOccurrenceId = null;
                }
            });
            if (skipped.ProducedOccurrenceId is { } produced)
            {
                tasks.Remove(produced);
            }

            tasks.Put(restored);
            return Task.FromResult(restored);
        }
    }

    private sealed record Setup(
        FakeTaskService Service, FakeRecurrenceService Recurrence, FakeDialogs Dialogs, MainViewModel Vm);

    private static async Task<Setup> StartAsync(params (string Title, DateOnly? Due, Recurrence? Recurrence)[] tasks)
    {
        var service = new FakeTaskService();
        foreach (var (title, due, recurrence) in tasks)
        {
            var task = await service.CreateTaskAsync(title, Ct);
            task.DueDate = due;
            task.Recurrence = recurrence;
        }

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        var recurrenceService = new FakeRecurrenceService(service);
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(
            service, new FakeTagService(service), clock, recurrenceService: recurrenceService,
            occurrenceDialogs: dialogs);
        await vm.LoadAsync(Ct);
        return new Setup(service, recurrenceService, dialogs, vm);
    }

    private static List<(string Title, DateOnly? Due)> Open(MainViewModel vm) =>
        [.. vm.Tasks.Select(t => (t.Title, t.Item.DueDate))];

    // ---- Rows ----

    [Fact]
    public async Task Recurring_rows_show_the_repeat_icon()
    {
        var s = await StartAsync(("daily", Day(20), Recurrence.Daily()), ("once", Day(20), null));

        Assert.True(s.Vm.Tasks[0].IsRecurring);
        Assert.False(s.Vm.Tasks[1].IsRecurring);
    }

    // ---- Completing and Ctrl+Z ----

    [Fact]
    public async Task Completing_an_Occurrence_shows_the_next_one()
    {
        var s = await StartAsync(("a", Day(20), Recurrence.Daily()), ("b", null, null));

        await s.Vm.ToggleCompleteCommand.ExecuteAsync(s.Vm.Tasks[0]);

        Assert.Equal([("a", Day(21)), ("b", null)], Open(s.Vm));
        Assert.Equal(["a"], s.Vm.CompletedTasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Ctrl_Z_right_after_completing_deletes_the_Occurrence_it_produced()
    {
        var s = await StartAsync(("a", Day(20), Recurrence.Daily()));
        var a = s.Vm.Tasks[0];
        await s.Vm.ToggleCompleteCommand.ExecuteAsync(a);
        var produced = s.Service.All.Single(t => t.Id != a.Id).Id;

        await s.Vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal([(a.Id, (Guid?)produced)], s.Recurrence.UndoCompletionCalls);
        Assert.Equal([("a", Day(20))], Open(s.Vm));
        Assert.Empty(s.Vm.CompletedTasks);
    }

    [Fact]
    public async Task A_later_manual_un_complete_keeps_the_next_Occurrence()
    {
        var s = await StartAsync(("a", Day(20), Recurrence.Daily()));
        var a = s.Vm.Tasks[0];
        await s.Vm.ToggleCompleteCommand.ExecuteAsync(a);

        await s.Vm.ToggleCompleteCommand.ExecuteAsync(s.Vm.CompletedTasks.Single());

        Assert.Empty(s.Recurrence.UndoCompletionCalls);
        Assert.Equal([("a", Day(20)), ("a", Day(21))], Open(s.Vm));
    }

    [Fact]
    public async Task Ctrl_Z_after_a_re_completion_that_produced_nothing_deletes_nothing()
    {
        var s = await StartAsync(("a", Day(20), Recurrence.Daily()));
        await s.Vm.ToggleCompleteCommand.ExecuteAsync(s.Vm.Tasks[0]);
        await s.Vm.ToggleCompleteCommand.ExecuteAsync(s.Vm.CompletedTasks.Single());
        await s.Vm.ToggleCompleteCommand.ExecuteAsync(s.Vm.Tasks[0]);

        await s.Vm.UndoCommand.ExecuteAsync(null);

        Assert.Empty(s.Recurrence.UndoCompletionCalls);
        Assert.Equal([("a", Day(20)), ("a", Day(21))], Open(s.Vm));
    }

    // ---- Search and filters ----

    // Matches on the title; the Today view keeps open Tasks due by 2026-10-20. Real rules: the Data tests.
    private sealed class FakeSearchService(FakeTaskService tasks) : ITaskSearchService
    {
        public List<TaskSearchQuery> Queries { get; } = [];

        public Task<IReadOnlyList<TaskItem>> SearchAsync(
            TaskSearchQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            IEnumerable<TaskItem> result = tasks.All.Where(t =>
                (string.IsNullOrWhiteSpace(query.Text)
                    || t.Title.Contains(query.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                && (query.View != SmartView.Today || (!t.IsCompleted && t.DueDate <= Day(20))));
            return Task.FromResult<IReadOnlyList<TaskItem>>([.. result]);
        }
    }

    private static async Task<(Setup Setup, FakeSearchService Search)> StartSearchingAsync(
        string searchText, params (string Title, DateOnly? Due, Recurrence? Recurrence)[] tasks)
    {
        var plain = await StartAsync(tasks);
        var search = new FakeSearchService(plain.Service);
        var vm = new MainViewModel(
            plain.Service, new FakeTagService(plain.Service), new FakeTimeProvider(), searchService: search,
            recurrenceService: plain.Recurrence, occurrenceDialogs: plain.Dialogs)
        { SearchText = searchText };
        await vm.ApplySearchCommand.ExecuteAsync(null);
        return (plain with { Vm = vm }, search);
    }

    [Fact]
    public async Task Search_results_show_the_repeat_icon()
    {
        var (s, _) = await StartSearchingAsync("water", ("water daily", Day(20), Recurrence.Daily()),
            ("water once", Day(20), null), ("other", Day(20), Recurrence.Daily()));

        Assert.Equal(["water daily", "water once"], s.Vm.Tasks.Select(t => t.Title));
        Assert.Equal([true, false], s.Vm.Tasks.Select(t => t.IsRecurring));
    }

    [Fact]
    public async Task Completing_an_Occurrence_during_a_search_shows_the_next_one_when_it_matches()
    {
        var (s, search) = await StartSearchingAsync("water", ("water", Day(20), Recurrence.Daily()),
            ("other", Day(20), null));
        var queries = search.Queries.Count;

        await s.Vm.ToggleCompleteCommand.ExecuteAsync(s.Vm.Tasks[0]);

        Assert.True(search.Queries.Count > queries); // reloaded through the search
        Assert.Equal([("water", Day(21))], Open(s.Vm));
        Assert.True(s.Vm.Tasks[0].IsRecurring);
    }

    [Fact]
    public async Task Completing_an_Occurrence_during_a_search_hides_a_next_one_outside_the_view()
    {
        var (s, search) = await StartSearchingAsync("water", ("water", Day(20), Recurrence.Daily()));
        await s.Vm.ShowSmartViewAsync(SmartView.Today, Ct);
        Assert.Equal([("water", Day(20))], Open(s.Vm));

        await s.Vm.ToggleCompleteCommand.ExecuteAsync(s.Vm.Tasks[0]);

        Assert.Equal(SmartView.Today, search.Queries.Last().View);
        Assert.Empty(s.Vm.Tasks); // the next Occurrence is due on the 21st, not Today
        Assert.Equal(2, s.Service.All.Count);
    }

    // ---- Deleting an Occurrence ----

    [Fact]
    public async Task Deleting_a_Task_without_a_Recurrence_does_not_ask()
    {
        var s = await StartAsync(("a", Day(20), null));

        await s.Vm.DeleteCommand.ExecuteAsync(s.Vm.Tasks[0]);

        Assert.Empty(s.Dialogs.Asked);
        Assert.Single(s.Service.Deleted);
        Assert.True(s.Vm.IsUndoBarVisible);
    }

    [Fact]
    public async Task Cancelling_the_delete_question_keeps_the_Occurrence()
    {
        var s = await StartAsync(("a", Day(20), Recurrence.Daily()));

        await s.Vm.DeleteCommand.ExecuteAsync(s.Vm.Tasks[0]);

        Assert.Equal(["a"], s.Dialogs.Asked);
        Assert.Empty(s.Service.Deleted);
        Assert.Empty(s.Recurrence.Skipped);
        Assert.Equal([("a", Day(20))], Open(s.Vm));
        Assert.False(s.Vm.IsUndoBarVisible);
    }

    [Fact]
    public async Task Delete_this_occurrence_skips_it_and_shows_the_next_one_and_undo_reverts_both()
    {
        var s = await StartAsync(("a", Day(20), Recurrence.Daily()));
        s.Dialogs.Answer = OccurrenceDeleteChoice.DeleteThisOccurrence;
        var a = s.Vm.Tasks[0];

        await s.Vm.DeleteCommand.ExecuteAsync(a);

        Assert.Equal([a.Id], s.Recurrence.Skipped);
        Assert.Equal([("a", Day(21))], Open(s.Vm));
        Assert.True(s.Vm.IsUndoBarVisible);

        await s.Vm.UndoCommand.ExecuteAsync(null);

        Assert.Single(s.Recurrence.UndoneSkips);
        Assert.Equal([a.Id], s.Vm.Tasks.Select(t => t.Id));
        Assert.False(s.Vm.IsUndoBarVisible);
    }

    [Fact]
    public async Task Stop_repeating_deletes_the_Occurrence_and_undo_restores_it()
    {
        var s = await StartAsync(("a", Day(20), Recurrence.Daily()));
        s.Dialogs.Answer = OccurrenceDeleteChoice.StopRepeating;
        var a = s.Vm.Tasks[0];

        await s.Vm.DeleteCommand.ExecuteAsync(a);

        Assert.Empty(s.Recurrence.Skipped);
        Assert.Equal([a.Id], s.Service.Deleted);
        Assert.Empty(s.Vm.Tasks);

        await s.Vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal([a.Id], s.Service.Restored);
        Assert.Equal([a.Id], s.Vm.Tasks.Select(t => t.Id));
    }

    // ---- Repeat editor in the detail pane ----

    [Fact]
    public async Task Selecting_a_recurring_Task_shows_its_Recurrence()
    {
        var weekly = Recurrence.Weekly(RecurrenceDays.Monday | RecurrenceDays.Friday, 2) with
        {
            Anchor = RecurrenceAnchor.CompletionDate,
            EndDate = Day(31),
        };
        var s = await StartAsync(("a", Day(20), weekly), ("b", Day(20), Recurrence.Weekdays()), ("c", Day(20), null));

        s.Vm.SelectedTask = s.Vm.Tasks[0];
        var repeat = s.Vm.Repeat;
        Assert.Equal(RepeatPreset.Weekly, repeat.Preset);
        Assert.Equal(2, repeat.Interval);
        Assert.True(repeat.Monday);
        Assert.True(repeat.Friday);
        Assert.False(repeat.Tuesday);
        Assert.True(repeat.RepeatFromCompletion);
        Assert.Equal(new DateTime(2026, 10, 31), repeat.EndDate);

        s.Vm.SelectedTask = s.Vm.Tasks[1];
        Assert.Equal(RepeatPreset.Weekdays, s.Vm.Repeat.Preset);

        s.Vm.SelectedTask = s.Vm.Tasks[2];
        Assert.Equal(RepeatPreset.None, s.Vm.Repeat.Preset);
        await s.Vm.PendingSave;
        Assert.Empty(s.Recurrence.SetCalls);
    }

    public static TheoryData<RepeatPreset> PresetCases => new()
    {
        RepeatPreset.Daily, RepeatPreset.Weekdays, RepeatPreset.Weekly, RepeatPreset.Monthly, RepeatPreset.Yearly,
    };

    [Theory]
    [MemberData(nameof(PresetCases))]
    public async Task Choosing_a_preset_saves_the_Recurrence_it_stands_for(RepeatPreset preset)
    {
        var s = await StartAsync(("a", Day(20), null));
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.Repeat.Preset = preset;
        await s.Vm.PendingSave;

        // The defaults come from the Due date, Tuesday 2026-10-20.
        Recurrence expected = preset switch
        {
            RepeatPreset.Daily => Recurrence.Daily(),
            RepeatPreset.Weekdays => Recurrence.Weekdays(),
            RepeatPreset.Weekly => Recurrence.Weekly(RecurrenceDays.Tuesday),
            RepeatPreset.Monthly => Recurrence.Monthly(20),
            _ => Recurrence.Yearly(10, 20),
        };
        Assert.Equal([(s.Vm.Tasks[0].Id, (Recurrence?)expected)], s.Recurrence.SetCalls);
        Assert.True(s.Vm.Tasks[0].IsRecurring);
    }

    [Fact]
    public async Task Every_N_days_on_chosen_days_from_the_completion_date_until_an_end_date_is_saved()
    {
        var s = await StartAsync(("a", Day(20), null));
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.Repeat.Preset = RepeatPreset.Weekly;
        s.Vm.Repeat.Thursday = true;
        s.Vm.Repeat.Interval = 3;
        s.Vm.Repeat.RepeatFromCompletion = true;
        s.Vm.Repeat.EndDate = new DateTime(2026, 12, 31);
        await s.Vm.PendingSave;

        var expected = Recurrence.Weekly(RecurrenceDays.Tuesday | RecurrenceDays.Thursday, 3) with
        {
            Anchor = RecurrenceAnchor.CompletionDate,
            EndDate = new DateOnly(2026, 12, 31),
        };
        Assert.Equal(expected, s.Recurrence.SetCalls[^1].Recurrence);
        Assert.Equal(expected, s.Service.All.Single().Recurrence);
    }

    [Fact]
    public async Task The_day_of_month_of_a_Monthly_Recurrence_can_be_changed()
    {
        var s = await StartAsync(("a", Day(20), Recurrence.Monthly(20)));
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.Repeat.DayOfMonth = 31;
        await s.Vm.PendingSave;

        Assert.Equal(Recurrence.Monthly(31), s.Recurrence.SetCalls[^1].Recurrence);
    }

    [Fact]
    public async Task Choosing_no_repeat_clears_the_Recurrence()
    {
        var s = await StartAsync(("a", Day(20), Recurrence.Daily()));
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.Repeat.Preset = RepeatPreset.None;
        await s.Vm.PendingSave;

        Assert.Equal([(s.Vm.Tasks[0].Id, (Recurrence?)null)], s.Recurrence.SetCalls);
        Assert.False(s.Vm.Tasks[0].IsRecurring);
    }

    [Fact]
    public async Task Repeating_needs_a_Due_date()
    {
        var s = await StartAsync(("a", null, null));
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.Repeat.Preset = RepeatPreset.Daily;
        await s.Vm.PendingSave;

        Assert.Empty(s.Recurrence.SetCalls);
        Assert.Equal("A repeating Task needs a Due date.", s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task An_invalid_interval_is_reported_and_not_saved()
    {
        var s = await StartAsync(("a", Day(20), Recurrence.Daily()));
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.Repeat.Interval = 0;
        await s.Vm.PendingSave;

        Assert.Empty(s.Recurrence.SetCalls);
        Assert.NotNull(s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task A_rejected_Repeat_is_shown_under_the_Repeat_field()
    {
        var s = await StartAsync(("a", Day(20), Recurrence.Daily()));
        s.Vm.SelectedTask = s.Vm.Tasks[0];

        s.Vm.Repeat.Interval = 0;
        await s.Vm.PendingSave;

        Assert.Equal(s.Vm.ErrorMessage, s.Vm.RepeatError);
        Assert.NotNull(s.Vm.RepeatError);
        Assert.Null(s.Vm.GeneralError);
    }
}
