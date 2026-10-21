using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

/// <summary>
/// Fills a database with a large, varied set of Tasks for performance checks. The Tasks have no Reminder and no
/// Nag, and "Notify at Due" is off on each, so running the app on the result cannot show a notification.
/// </summary>
public static class SampleDataGenerator
{
    private static readonly string[] TagNames =
        ["work", "home", "errand", "idea", "waiting", "urgent", "read", "call", "money", "health"];

    /// <summary>
    /// Creates (or migrates) the database at <paramref name="paths"/> and adds <paramref name="taskCount"/>
    /// Tasks to its Default List: a fifth completed, a third with Due dates, some with Tags and Subtasks.
    /// </summary>
    public static async Task GenerateAsync(IAppPaths paths, int taskCount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(taskCount);

        var services = new ServiceCollection();
        services.AddTaskDeskData(paths);
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(cancellationToken);

        var clock = provider.GetRequiredService<TimeProvider>();
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        await using var context = await provider.GetRequiredService<IDbContextFactory<TaskDeskDbContext>>()
            .CreateDbContextAsync(cancellationToken);
        var list = await context.Lists.SingleAsync(l => l.IsDefault, cancellationToken);

        var tags = TagNames.Select(name => new Tag
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            NormalizedName = TagName.Normalize(name),
        }).ToList();
        context.Tags.AddRange(tags);

        for (var i = 0; i < taskCount; i++)
        {
            var completed = i % 5 == 0;
            var task = new TaskItem
            {
                Id = Guid.CreateVersion7(),
                ListId = list.Id,
                Title = $"Sample task {i + 1:00000} with a reasonably long title to fill the row",
                Notes = i % 7 == 0 ? "Some notes for this sample task." : null,
                Priority = (Priority)(i % 4),
                IsCompleted = completed,
                CompletedUtc = completed ? now.AddMinutes(-i) : null,
                DueDate = i % 3 == 0 ? today.AddDays((i % 61) - 30) : null,
                NotifyAtDue = false,
                SortOrder = i + 1,
                CreatedUtc = now.AddMinutes(-taskCount + i),
                UpdatedUtc = now,
            };
            if (i % 4 == 0)
            {
                task.Tags.Add(tags[i % tags.Count]);
            }

            if (i % 10 == 0)
            {
                for (var s = 0; s < 3; s++)
                {
                    task.Subtasks.Add(new Subtask
                    {
                        Id = Guid.CreateVersion7(),
                        Title = $"Step {s + 1}",
                        IsCompleted = s == 0,
                        SortOrder = s + 1,
                    });
                }
            }

            context.Tasks.Add(task);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
