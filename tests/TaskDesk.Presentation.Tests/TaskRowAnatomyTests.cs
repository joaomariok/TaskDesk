using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

public class TaskRowAnatomyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TaskList Work = new() { Id = Guid.NewGuid(), Name = "Work", ColorHex = "#0078D4" };
    private static readonly TaskList Plain = new() { Id = Guid.NewGuid(), Name = "Plain" };

    private static TaskItem Item(params string[] tags) => new()
    {
        Id = Guid.NewGuid(),
        Title = "t",
        Tags = [.. tags.Select(n => new Tag { Id = Guid.NewGuid(), Name = n })],
    };

    private sealed class FakeListService(params TaskList[] lists) : IListService
    {
        public Task<IReadOnlyList<ListSummary>> GetListsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ListSummary>>([.. lists.Select(l => new ListSummary(l, 0))]);

        public Task<TaskList> CreateListAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RenameListAsync(Guid listId, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetListColorAsync(Guid listId, string? colorHex, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ReorderListsAsync(IReadOnlyList<Guid> orderedListIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteListAsync(Guid listId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    // Lists every Task for any Smart View; the views are not under test here.
    private sealed class FakeDueService(FakeTaskService tasks) : IDueService
    {
        public Task<TaskItem> SetDueAsync(
            Guid taskId, DateOnly? dueDate, TimeOnly? dueTime, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SmartViewCounts> CountSmartViewsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TaskItem>> ListSmartViewAsync(
            SmartView view, int skip = 0, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>([.. tasks.All]);
    }

    private static async Task<(FakeTaskService Service, MainViewModel Vm, FakeTimeProvider Clock)> StartAsync()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero));
        var service = new FakeTaskService();
        var task = await service.CreateTaskAsync("one", Ct);
        task.ListId = Work.Id;
        var vm = new MainViewModel(
            service, new FakeTagService(service), clock, new FakeDueService(service),
            listService: new FakeListService(Work, Plain));
        return (service, vm, clock);
    }

    // ---- The row's own data ----

    [Fact]
    public void A_row_knows_its_List_name_and_colour()
    {
        var row = new TaskRowViewModel(Item(), list: Work);

        Assert.Equal("Work", row.ListName);
        Assert.Equal("#0078D4", row.ListColorHex);
        Assert.True(row.HasListColor);
    }

    [Fact]
    public void A_colourless_List_gives_no_stripe()
    {
        var row = new TaskRowViewModel(Item(), list: Plain);

        Assert.Null(row.ListColorHex);
        Assert.False(row.HasListColor);
    }

    [Fact]
    public void A_row_without_a_known_List_has_no_stripe_and_no_caption()
    {
        var row = new TaskRowViewModel(Item(), showListName: true);

        Assert.False(row.HasListColor);
        Assert.False(row.ShowListName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_List_caption_follows_the_flag(bool show)
    {
        var row = new TaskRowViewModel(Item(), list: Work, showListName: show);

        Assert.Equal(show, row.ShowListName);
    }

    [Theory]
    [InlineData(0, 0, 0, "")]
    [InlineData(2, 2, 0, "")]
    [InlineData(5, 2, 3, "+3")]
    public void Tags_beyond_the_second_collapse_into_a_count(int tags, int visible, int overflow, string text)
    {
        var row = new TaskRowViewModel(Item([.. Enumerable.Range(1, tags).Select(i => $"tag{i}")]));

        Assert.Equal(visible, row.VisibleTags.Count);
        Assert.Equal(overflow, row.TagOverflowCount);
        Assert.Equal(overflow > 0, row.HasTagOverflow);
        Assert.Equal(text, row.TagOverflowText);
    }

    [Fact]
    public void The_visible_Tags_are_the_first_two()
    {
        var row = new TaskRowViewModel(Item("a", "b", "c"));

        Assert.Equal(["a", "b"], row.VisibleTags.Select(t => t.Name));
    }

    [Fact]
    public async Task Adding_Tags_to_a_Task_updates_its_chips_and_the_count()
    {
        var (_, vm, _) = await StartAsync();
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks.Single();

        foreach (var name in new[] { "a", "b", "c" })
        {
            vm.NewTagName = name;
            await vm.AddTagCommand.ExecuteAsync(null);
        }

        // Each Tag change reloads the rows.
        var row = vm.Tasks.Single();
        Assert.Equal(["a", "b"], row.VisibleTags.Select(t => t.Name));
        Assert.Equal("+1", row.TagOverflowText);
    }

    [Theory]
    [InlineData(Priority.None, false, "")]
    [InlineData(Priority.Low, true, "Low priority")]
    [InlineData(Priority.Medium, true, "Medium priority")]
    [InlineData(Priority.High, true, "High priority")]
    public void The_Priority_flag_shows_for_every_level_but_None(Priority priority, bool flag, string label)
    {
        var item = Item();
        item.Priority = priority;
        var row = new TaskRowViewModel(item);

        Assert.Equal(priority, row.Priority);
        Assert.Equal(flag, row.HasPriorityFlag);
        Assert.Equal(label, row.PriorityLabel);
    }

    // ---- The List caption per view ----

    [Fact]
    public async Task Inside_a_List_the_rows_have_no_List_caption_but_keep_the_stripe()
    {
        var (_, vm, _) = await StartAsync();

        await vm.LoadAsync(Ct);

        var row = vm.Tasks.Single();
        Assert.False(row.ShowListName);
        Assert.Equal("#0078D4", row.ListColorHex);
    }

    [Theory]
    [InlineData(SmartView.Today)]
    [InlineData(SmartView.Upcoming)]
    [InlineData(SmartView.Overdue)]
    [InlineData(SmartView.All)]
    [InlineData(SmartView.Completed)]
    public async Task Every_Smart_View_captions_its_rows_with_the_List_name(SmartView view)
    {
        var (_, vm, _) = await StartAsync();

        await vm.ShowSmartViewAsync(view, Ct);

        var row = vm.Tasks.Concat(vm.CompletedTasks).Concat(vm.OverdueTasks).Single();
        Assert.True(row.ShowListName);
        Assert.Equal("Work", row.ListName);
    }

    [Fact]
    public async Task A_Tags_page_captions_its_rows_with_the_List_name()
    {
        var (service, vm, _) = await StartAsync();
        var tags = new FakeTagService(service);
        vm = new MainViewModel(service, tags, listService: new FakeListService(Work, Plain));
        await tags.AddTagToTaskAsync(service.All.Single().Id, "home", Ct);
        await vm.LoadAsync(Ct);

        await vm.FilterByTagAsync(vm.Tags.Single(), Ct);

        Assert.True(vm.Tasks.Single().ShowListName);
    }

    [Fact]
    public async Task Leaving_a_Smart_View_for_the_List_drops_the_caption_again()
    {
        var (_, vm, _) = await StartAsync();
        await vm.ShowSmartViewAsync(SmartView.All, Ct);

        await vm.ShowSmartViewAsync(null, Ct);

        Assert.False(vm.Tasks.Single().ShowListName);
    }

    // ---- Completion ----

    private static async Task<TaskRowViewModel> CompleteAsync(MainViewModel vm)
    {
        await vm.LoadAsync(Ct);
        var row = vm.Tasks.Single();
        await vm.ToggleCompleteCommand.ExecuteAsync(row);
        return row;
    }

    [Fact]
    public async Task Without_a_delay_a_completed_Task_moves_at_once()
    {
        var (_, vm, _) = await StartAsync();

        var row = await CompleteAsync(vm);

        Assert.Empty(vm.Tasks);
        Assert.Same(row, vm.CompletedTasks.Single());
    }

    [Fact]
    public async Task With_a_delay_the_Task_is_saved_and_struck_at_once_and_moves_afterwards()
    {
        var (service, vm, clock) = await StartAsync();
        vm.CompletionMoveDelay = () => TimeSpan.FromMilliseconds(400);

        var row = await CompleteAsync(vm);

        Assert.Single(service.Completions);
        Assert.True(row.IsCompleted);
        Assert.Same(row, vm.Tasks.Single());
        Assert.Empty(vm.CompletedTasks);

        clock.Advance(TimeSpan.FromMilliseconds(400));
        await vm.CompletionMove;

        Assert.Empty(vm.Tasks);
        Assert.Same(row, vm.CompletedTasks.Single());
    }

    [Fact]
    public async Task Undo_during_the_delay_keeps_the_Task_open_and_is_not_held_up()
    {
        var (service, vm, clock) = await StartAsync();
        vm.CompletionMoveDelay = () => TimeSpan.FromMilliseconds(400);
        var row = await CompleteAsync(vm);

        await vm.UndoCommand.ExecuteAsync(null);
        clock.Advance(TimeSpan.FromMilliseconds(400));
        await vm.CompletionMove;

        Assert.Equal([(row.Id, true), (row.Id, false)], service.Completions);
        Assert.False(row.IsCompleted);
        Assert.Same(row, vm.Tasks.Single());
        Assert.Empty(vm.CompletedTasks);
    }

    [Fact]
    public async Task Un_completing_never_waits()
    {
        var (_, vm, _) = await StartAsync();
        var row = await CompleteAsync(vm);
        vm.CompletionMoveDelay = () => TimeSpan.FromMilliseconds(400);

        await vm.ToggleCompleteCommand.ExecuteAsync(row);

        Assert.Same(row, vm.Tasks.Single());
        Assert.Empty(vm.CompletedTasks);
    }
}
