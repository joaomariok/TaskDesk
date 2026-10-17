using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using Windows.Foundation;

namespace TaskDesk.Views.Tests;

/// <summary>
/// Drag to reorder in the open Task list: where a drop lands maps to the right move through the view model's
/// move use case, the order is persisted, and reordering is off outside a plain List view. The real mouse drag
/// is a FlaUI test (ReorderTests).
/// </summary>
[TestClass]
public sealed class TaskReorderTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task Dropping_a_row_on_another_moves_it_there_and_the_order_is_persisted()
    {
        using var db = await DatabaseAsync();
        foreach (var title in new[] { "A", "B", "C", "D" })
        {
            await AddAsync(db, title);
        }

        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            await UiWait.UntilAsync(() => viewModel.Tasks.Count == 4, () => "The Tasks did not load.", Ct);
            var taskList = window.View.TaskList;

            await taskList.DropAsync(viewModel.Tasks[0], viewModel.Tasks[2]);
            CollectionAssert.AreEqual(new[] { "B", "C", "A", "D" }, viewModel.Tasks.Select(t => t.Title).ToArray());
            CollectionAssert.AreEqual(new[] { "B", "C", "A", "D" }, await StoredOrderAsync(db));

            await taskList.DropAsync(viewModel.Tasks[3], viewModel.Tasks[0]);
            CollectionAssert.AreEqual(new[] { "D", "B", "C", "A" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Dropping_on_empty_space_moves_the_row_to_the_end()
    {
        using var db = await DatabaseAsync();
        foreach (var title in new[] { "A", "B", "C" })
        {
            await AddAsync(db, title);
        }

        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            await UiWait.UntilAsync(() => viewModel.Tasks.Count == 3, () => "The Tasks did not load.", Ct);

            await window.View.TaskList.DropAsync(viewModel.Tasks[0], null);

            CollectionAssert.AreEqual(new[] { "B", "C", "A" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_drop_position_maps_to_the_row_under_it_or_to_none_below_the_rows()
    {
        using var db = await DatabaseAsync();
        foreach (var title in new[] { "A", "B", "C" })
        {
            await AddAsync(db, title);
        }

        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
            await UiWait.UntilAsync(
                () => Enumerable.Range(0, 3).All(i => list.ContainerFromIndex(i) is FrameworkElement { ActualHeight: > 0 }),
                () => "The rows were not realized.", Ct);
            var taskList = window.View.TaskList;

            for (var i = 0; i < 3; i++)
            {
                var row = (FrameworkElement)list.ContainerFromIndex(i);
                var top = row.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y;
                Assert.AreSame(viewModel.Tasks[i], taskList.RowAt(new Point(20, top + (row.ActualHeight / 2))), $"Row {i}.");
            }

            Assert.IsNull(taskList.RowAt(new Point(20, list.ActualHeight + 500)));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Dropping_does_nothing_where_reordering_is_off()
    {
        using var db = await DatabaseAsync();
        var today = DateOnly.FromDateTime(db.Clock.GetUtcNow().Date);
        await AddAsync(db, "A", due: today);
        await AddAsync(db, "B", due: today);
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.ShowSmartViewAsync(SmartView.Today, Ct);
            await UiWait.UntilAsync(() => viewModel.Tasks.Count == 2, () => "The Tasks did not load.", Ct);
            Assert.IsFalse(viewModel.IsReorderEnabled);

            await window.View.TaskList.DropAsync(viewModel.Tasks[0], viewModel.Tasks[1]);

            CollectionAssert.AreEqual(new[] { "A", "B" }, await StoredOrderAsync(db));
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task<string[]> StoredOrderAsync(TempDatabase db) =>
        [.. (await db.QueryAsync(c => c.Tasks.OrderBy(t => t.SortOrder))).Select(t => t.Title)];

    private static (MainWindow Window, MainViewModel ViewModel) Open(TempDatabase db)
    {
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock,
            dueService: db.Get<IDueService>(), searchService: db.Get<ITaskSearchService>(),
            listService: db.Get<IListService>());
        var window = new MainWindow(viewModel);
        window.Activate();
        return (window, viewModel);
    }

    private async Task<TempDatabase> DatabaseAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return db;
    }

    private static async Task AddAsync(TempDatabase db, string title, DateOnly? due = null)
    {
        var listId = (await db.QueryAsync(c => c.Lists.Where(l => l.IsDefault))).Single().Id;
        await using var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync();
        var now = db.Clock.GetUtcNow().UtcDateTime;
        var order = await context.Tasks.CountAsync();
        context.Tasks.Add(new TaskItem
        {
            Id = Guid.CreateVersion7(),
            ListId = listId,
            Title = title,
            DueDate = due,
            SortOrder = order,
            CreatedUtc = now,
            UpdatedUtc = now,
        });
        await context.SaveChangesAsync();
    }
}
