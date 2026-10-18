using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data.Tests;

/// <summary>
/// Every public query that returns Tasks loads them through the shared path: Tags ordered by normalized name,
/// Subtasks ordered by sort order, and the owned Recurrence.
/// </summary>
public class TaskQueryDetailsTests
{
    private const string Expected = "zeta-first: Alpha,Zeta | first,second | Daily";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => new(2026, 10, 16);

    private static string Describe(TaskItem task) =>
        $"{task.Title}: {string.Join(',', task.Tags.Select(t => t.Name))}"
        + $" | {string.Join(',', task.Subtasks.Select(s => s.Title))}"
        + $" | {task.Recurrence?.Frequency.ToString() ?? "none"}";

    private static void AssertDetails(string query, TaskItem task) =>
        Assert.Equal($"{query} => {Expected}", $"{query} => {Describe(task)}");

    private static void AssertDetails(string query, IReadOnlyList<TaskItem> tasks, Guid id) =>
        AssertDetails(query, Assert.Single(tasks, t => t.Id == id));

    // Tags and Subtasks are added in the reverse of their expected order, so an unordered load shows up.
    private static async Task<TaskItem> AddDetailedTaskAsync(TempDatabase db)
    {
        var task = await db.Get<ITaskService>().CreateTaskAsync("zeta-first", Ct);
        await db.Get<ITagService>().AddTagToTaskAsync(task.Id, "Zeta", Ct);
        await db.Get<ITagService>().AddTagToTaskAsync(task.Id, "Alpha", Ct);
        await db.Get<ITaskService>().AddSubtaskAsync(task.Id, "second", Ct);
        var first = await db.Get<ITaskService>().AddSubtaskAsync(task.Id, "first", Ct);
        await db.Get<ITaskService>().MoveSubtaskAsync(first.Id, 0, Ct);
        await db.Get<IDueService>().SetDueAsync(task.Id, Today, null, Ct);
        return await db.Get<IRecurrenceService>().SetRecurrenceAsync(task.Id, Recurrence.Daily(), Ct);
    }

    [Fact]
    public async Task Every_public_query_returning_Tasks_loads_Tags_Subtasks_and_Recurrence()
    {
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        var tasks = db.Get<ITaskService>();
        var due = db.Get<IDueService>();
        var search = db.Get<ITaskSearchService>();
        var reminders = db.Get<IReminderService>();
        var recurrence = db.Get<IRecurrenceService>();

        var task = await AddDetailedTaskAsync(db);
        var id = task.Id;
        var tagId = task.Tags.Single(t => t.Name == "Alpha").Id;
        AssertDetails("SetRecurrenceAsync", task);

        AssertDetails("UpdateTaskAsync", await tasks.UpdateTaskAsync(id, "zeta-first", "notes", Priority.High, Ct));
        AssertDetails("ListDefaultListTasksAsync", await tasks.ListDefaultListTasksAsync(Ct), id);
        AssertDetails("ListTasksInListAsync", await tasks.ListTasksInListAsync(task.ListId, Ct), id);
        AssertDetails("ListTasksByTagAsync", await db.Get<ITagService>().ListTasksByTagAsync(tagId, Ct), id);
        AssertDetails("SearchAsync (Default List)", await search.SearchAsync(new(Text: "zeta"), Ct), id);
        AssertDetails("SearchAsync (Tag)", await search.SearchAsync(new(ViewTagId: tagId), Ct), id);
        AssertDetails(
            "SearchAsync (Smart View)", await search.SearchAsync(new(Text: "zeta", View: SmartView.All), Ct), id);

        AssertDetails("ListSmartViewAsync(Today)", await due.ListSmartViewAsync(SmartView.Today, 0, Ct), id);
        AssertDetails("ListSmartViewAsync(All)", await due.ListSmartViewAsync(SmartView.All, 0, Ct), id);
        AssertDetails("SetDueAsync (overdue)", await due.SetDueAsync(id, Today.AddDays(-1), null, Ct));
        AssertDetails("ListSmartViewAsync(Overdue)", await due.ListSmartViewAsync(SmartView.Overdue, 0, Ct), id);
        AssertDetails("SetDueAsync (upcoming)", await due.SetDueAsync(id, Today.AddDays(1), null, Ct));
        AssertDetails("ListSmartViewAsync(Upcoming)", await due.ListSmartViewAsync(SmartView.Upcoming, 0, Ct), id);

        var remindAt = db.Clock.GetUtcNow().UtcDateTime.AddHours(2);
        AssertDetails("SetReminderAsync", await reminders.SetReminderAsync(id, remindAt, Ct));
        AssertDetails("SnoozeAsync", await reminders.SnoozeAsync(id, TimeSpan.FromMinutes(10), Ct));
        AssertDetails("ClearSnoozeAsync", await reminders.ClearSnoozeAsync(id, Ct));

        var completed = await tasks.SetCompletedAsync(id, true, Ct);
        AssertDetails("SetCompletedAsync", completed);
        AssertDetails("ListSmartViewAsync(Completed)", await due.ListSmartViewAsync(SmartView.Completed, 0, Ct), id);
        AssertDetails("UndoCompletionAsync", await recurrence.UndoCompletionAsync(id, completed.NextOccurrenceId, Ct));

        var skipped = await recurrence.SkipOccurrenceAsync(id, Ct);
        AssertDetails("UndoSkipAsync", await recurrence.UndoSkipAsync(skipped, Ct));

        var deleted = await tasks.DeleteTaskAsync(id, Ct);
        AssertDetails("RestoreTaskAsync", await tasks.RestoreTaskAsync(deleted, Ct));
    }
}
