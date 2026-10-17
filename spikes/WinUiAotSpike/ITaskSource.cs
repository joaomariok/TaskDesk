using TaskDesk.Core.Tasks;

namespace WinUiAotSpike;

/// <summary>What the spike window needs: the Default List's Tasks and quick-add.</summary>
public interface ITaskSource
{
    Task<IReadOnlyList<TaskItem>> ListDefaultListTasksAsync();

    Task<TaskItem> CreateTaskAsync(string title);
}

/// <summary>The real path: the product's <see cref="ITaskService"/> from TaskDesk.Data.</summary>
internal sealed class ProductTaskSource(ITaskService tasks) : ITaskSource
{
    public Task<IReadOnlyList<TaskItem>> ListDefaultListTasksAsync() => tasks.ListDefaultListTasksAsync();

    public Task<TaskItem> CreateTaskAsync(string title) => tasks.CreateTaskAsync(title);
}

/// <summary>
/// <c>--no-data</c>: 4,000 in-memory open Tasks (the open count of the 5,000-Task sample database), no EF Core.
/// Measures the WinUI shell on its own where EF Core cannot run (Native AOT without a compiled model).
/// </summary>
internal sealed class InMemoryTaskSource : ITaskSource
{
    private const int OpenTaskCount = 4000;

    public Task<IReadOnlyList<TaskItem>> ListDefaultListTasksAsync()
    {
        var now = DateTime.UtcNow;
        var listId = Guid.CreateVersion7();
        var tasks = new TaskItem[OpenTaskCount];
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = new TaskItem
            {
                Id = Guid.CreateVersion7(),
                ListId = listId,
                Title = $"Sample task {i + 1:00000} with a reasonably long title to fill the row",
                SortOrder = i + 1,
                CreatedUtc = now,
                UpdatedUtc = now,
            };
        }

        return Task.FromResult<IReadOnlyList<TaskItem>>(tasks);
    }

    public Task<TaskItem> CreateTaskAsync(string title)
    {
        var now = DateTime.UtcNow;
        return Task.FromResult(new TaskItem
        {
            Id = Guid.CreateVersion7(),
            Title = TaskTitle.Normalize(title),
            CreatedUtc = now,
            UpdatedUtc = now,
        });
    }
}
