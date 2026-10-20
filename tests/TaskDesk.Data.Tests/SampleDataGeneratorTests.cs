using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

public class SampleDataGeneratorTests
{
    [Fact]
    public async Task Generates_the_requested_number_of_Tasks_in_the_Default_List()
    {
        using var db = new TempDatabase();
        var ct = TestContext.Current.CancellationToken;

        await SampleDataGenerator.GenerateAsync(db.Paths, 5000, ct);

        var tasks = await db.QueryAsync(c => c.Tasks);
        var defaultList = Assert.Single(await db.QueryAsync(c => c.Lists));
        Assert.Equal(5000, tasks.Count);
        Assert.All(tasks, t => Assert.Equal(defaultList.Id, t.ListId));
        Assert.Equal(5000, tasks.Select(t => t.SortOrder).Distinct().Count());
    }

    [Fact]
    public async Task Generated_data_has_no_Reminders_that_could_fire()
    {
        using var db = new TempDatabase();
        var ct = TestContext.Current.CancellationToken;

        await SampleDataGenerator.GenerateAsync(db.Paths, 300, ct);

        var tasks = await db.QueryAsync(c => c.Tasks.Include(t => t.Reminders));
        Assert.All(tasks, t =>
        {
            Assert.Empty(t.Reminders);
            Assert.Null(t.SnoozedUntilUtc);
        });
    }

    [Fact]
    public async Task Generated_data_mixes_completed_Tasks_Tags_Subtasks_and_Due_dates()
    {
        using var db = new TempDatabase();
        var ct = TestContext.Current.CancellationToken;

        await SampleDataGenerator.GenerateAsync(db.Paths, 500, ct);

        var tasks = await db.QueryAsync(c => c.Tasks.Include(t => t.Tags).Include(t => t.Subtasks));
        Assert.Contains(tasks, t => t.IsCompleted);
        Assert.Contains(tasks, t => !t.IsCompleted);
        Assert.Contains(tasks, t => t.Tags.Count > 0);
        Assert.Contains(tasks, t => t.Subtasks.Count > 0);
        Assert.Contains(tasks, t => t.DueDate is not null);
        Assert.Contains(tasks, t => t.Priority == Priority.High);
    }

    [Fact]
    public async Task Rejects_a_negative_count()
    {
        using var db = new TempDatabase();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => SampleDataGenerator.GenerateAsync(db.Paths, -1, TestContext.Current.CancellationToken));
    }
}
