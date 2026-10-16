using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public class SubtaskProgressTests
{
    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("0/1", 0, 1)]
    [InlineData("2/5", 2, 5)]
    [InlineData("5/5", 5, 5)]
    public void Formats_done_over_total_and_nothing_when_there_are_no_Subtasks(string expected, int done, int total)
    {
        var subtasks = Enumerable.Range(0, total).Select(i => new Subtask { IsCompleted = i < done });

        Assert.Equal(expected, SubtaskProgress.Format(subtasks));
    }
}
