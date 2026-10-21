using Microsoft.Extensions.Time.Testing;
using TaskDesk.Presentation;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

/// <summary>
/// Dragging a Task when some Tasks are open: an open Task and its Subtask rows are one block that moves together
/// and that a drop never splits. Gaps here are over the open group's rows, Subtask rows included.
/// </summary>
public class TaskBlockMoveViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // "a" (Subtask a1), "b" (b1, b2), "c" (none) and "d" (d1), with the Tasks in "open" opened.
    private static async Task<(FakeTaskService Service, MainViewModel Vm)> StartAsync(string open)
    {
        var service = new FakeTaskService();
        var subtasks = new Dictionary<string, string[]>
        {
            ["a"] = ["a1"],
            ["b"] = ["b1", "b2"],
            ["c"] = [],
            ["d"] = ["d1"],
        };
        foreach (var (title, children) in subtasks)
        {
            var task = await service.CreateTaskAsync(title, Ct);
            foreach (var child in children)
            {
                await service.AddSubtaskAsync(task.Id, child, Ct);
            }
        }

        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());
        await vm.LoadAsync(Ct);
        foreach (var title in open.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            vm.OpenTask(vm.Tasks.Single(t => t.Title == title));
        }

        return (service, vm);
    }

    // The open group's rows: a Task by its title, a Subtask row as "-" and its title.
    private static string Rows(MainViewModel vm) =>
        string.Join('|', vm.Groups.Single(g => g.Kind == TaskGroupKind.Open)
            .Select(r => r is SubtaskListRowViewModel ? "-" + r.Title : r.Title));

    [Theory]
    // Another Task (b) open, rows a|b|-b1|-b2|c|d.
    [InlineData("b", "d", 1, "a|d|b|-b1|-b2|c", true)] // before b's block
    [InlineData("b", "d", 2, "a|b|-b1|-b2|d|c", true)] // after b's Task row: after the block
    [InlineData("b", "d", 3, "a|b|-b1|-b2|d|c", true)] // between b's Subtasks: after the block
    [InlineData("b", "d", 4, "a|b|-b1|-b2|d|c", true)] // after the block
    [InlineData("b", "d", 9, "a|b|-b1|-b2|c|d", false)] // past the rows: d is already last
    [InlineData("b", "a", 3, "b|-b1|-b2|a|c|d", true)] // dragged down into b's block: after it
    [InlineData("b", "a", 6, "b|-b1|-b2|c|d|a", true)] // after the last row
    // The same Task closed: its Subtasks are not rows, so gap 2 is between b and c.
    [InlineData("", "d", 2, "a|b|d|c", true)]
    [InlineData("", "d", 1, "a|d|b|c", true)]
    // The dragged Task itself open: its own block's gaps and the gaps on either side change nothing.
    [InlineData("b", "b", 1, "a|b|-b1|-b2|c|d", false)] // above it
    [InlineData("b", "b", 2, "a|b|-b1|-b2|c|d", false)] // inside its own block
    [InlineData("b", "b", 3, "a|b|-b1|-b2|c|d", false)] // inside its own block
    [InlineData("b", "b", 4, "a|b|-b1|-b2|c|d", false)] // below it
    [InlineData("b", "b", 0, "b|-b1|-b2|a|c|d", true)] // before the first row, Subtasks along
    [InlineData("b", "b", 5, "a|c|b|-b1|-b2|d", true)] // between c and d, Subtasks along
    [InlineData("b", "b", 6, "a|c|d|b|-b1|-b2", true)] // to the end, Subtasks along
    // Two open blocks, the last one open: rows a|-a1|b|c|d|-d1.
    [InlineData("a,d", "b", 1, "a|-a1|b|c|d|-d1", false)] // inside a's block: after it, b's own gap
    [InlineData("a,d", "c", 1, "a|-a1|c|b|d|-d1", true)] // inside a's block: after it
    [InlineData("a,d", "a", 5, "b|c|d|-d1|a|-a1", true)] // inside the last block: the end
    [InlineData("a,d", "a", 6, "b|c|d|-d1|a|-a1", true)] // after the last block
    [InlineData("a,d", "b", 9, "a|-a1|c|d|-d1|b", true)] // past the rows: the end
    [InlineData("a,d", "d", 0, "d|-d1|a|-a1|b|c", true)] // an open last block to the front
    [InlineData("a,d", "d", 5, "a|-a1|b|c|d|-d1", false)] // inside its own block
    public async Task Moving_a_Task_into_a_row_gap_lands_after_any_block_the_gap_is_inside(
        string open, string dragged, int rowGap, string expected, bool moves)
    {
        var (service, vm) = await StartAsync(open);
        var row = vm.Tasks.Single(t => t.Title == dragged);

        await vm.MoveTaskToGapAsync(row, vm.TaskGapOfRowGap(rowGap), Ct);

        Assert.Equal(expected, Rows(vm));
        Assert.Equal(moves ? 1 : 0, service.Moves.Count);
        var order = expected.Split('|').Where(r => !r.StartsWith('-'));
        Assert.Equal(order, service.All.Select(t => t.Title));
    }

    [Theory]
    [InlineData("b", 0, 0)]
    [InlineData("b", 1, 1)]
    [InlineData("b", 2, 4)] // b's block is three rows
    [InlineData("b", 3, 5)]
    [InlineData("b", 4, 6)] // the last gap, under the last row
    [InlineData("b", 9, 6)] // past the Tasks means the last gap
    [InlineData("a,d", 4, 6)] // under the open last block's last Subtask row
    [InlineData("a,d", 1, 2)]
    [InlineData("", 2, 2)]
    public async Task A_Task_gap_sits_at_the_row_gap_after_whole_blocks(string open, int taskGap, int expected)
    {
        var (_, vm) = await StartAsync(open);

        Assert.Equal(expected, vm.RowGapOfTaskGap(taskGap));
    }
}
