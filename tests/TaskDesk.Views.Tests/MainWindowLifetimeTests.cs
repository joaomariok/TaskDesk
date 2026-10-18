using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using TaskDesk.Views.Dialogs;

namespace TaskDesk.Views.Tests;

// Once the main window has closed, nothing reloads its view model.
[TestClass]
public sealed class MainWindowLifetimeTests
{
    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task Once_the_window_has_closed_a_List_edit_a_quick_add_or_an_import_does_not_reload_it()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var inbox = (await db.QueryAsync(c => c.Lists)).Single().Id;
        await db.AddTaskAsync(inbox, "Pay rent", completed: false);
        var tags = new CountingTagService(db.Get<ITagService>());
        MainWindow? window = null;
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), tags, db.Clock, db.Get<IDueService>(), db.Get<ITaskSearchService>(),
            listService: db.Get<IListService>());
        var lists = new ListsViewModel(db.Get<IListService>(), new WinUiDialogs(new DialogService(() => window)));
        window = new MainWindow(viewModel, lists);
        try
        {
            window.Activate();
            await UiWait.UntilAsync(() => window.View.IsLoaded, () => "The main view never loaded.", ct);
            await viewModel.LoadAsync(ct);
            await lists.LoadAsync(ct);
        }
        finally
        {
            window.Close();
        }

        var loads = tags.ListTagsCalls;
        await db.AddTaskAsync(inbox, "Added after close", completed: false);

        // A recoloured List raises ListEdited, which the window turns into a reload.
        await lists.SetColorAsync(lists.Lists.Single(), "#3366CC", ct);
        Assert.AreEqual(loads, tags.ListTagsCalls, "A List edit reloaded the closed window.");

        // The quick-add card's Task created and the Settings import reload the main window with no token.
        await viewModel.LoadAsync();
        Assert.AreEqual(loads, tags.ListTagsCalls, "A quick-add or an import reloaded the closed window.");

        CollectionAssert.AreEqual(new[] { "Pay rent" }, viewModel.Tasks.Select(t => t.Title).ToList());
    }

    // Counts the page loads: every load lists the Tags first.
    private sealed class CountingTagService(ITagService inner) : ITagService
    {
        public int ListTagsCalls { get; private set; }

        public Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken cancellationToken = default)
        {
            ListTagsCalls++;
            return inner.ListTagsAsync(cancellationToken);
        }

        public Task<Tag> CreateTagAsync(string name, CancellationToken cancellationToken = default) =>
            inner.CreateTagAsync(name, cancellationToken);

        public Task<Tag> AddTagToTaskAsync(Guid taskId, string name, CancellationToken cancellationToken = default) =>
            inner.AddTagToTaskAsync(taskId, name, cancellationToken);

        public Task RemoveTagFromTaskAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default) =>
            inner.RemoveTagFromTaskAsync(taskId, tagId, cancellationToken);

        public Task DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default) =>
            inner.DeleteTagAsync(tagId, cancellationToken);

        public Task<IReadOnlyList<TaskItem>> ListTasksByTagAsync(Guid tagId, CancellationToken cancellationToken = default) =>
            inner.ListTasksByTagAsync(tagId, cancellationToken);
    }
}
