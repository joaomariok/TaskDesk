using System.Collections.Specialized;
using Microsoft.Extensions.Time.Testing;
using TaskDesk.Presentation;
using FakeTagService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTagService;
using FakeTaskService = TaskDesk.Presentation.Tests.MainViewModelTests.FakeTaskService;

namespace TaskDesk.Presentation.Tests;

public class TaskGroupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Expanding_and_collapsing_Completed_adds_and_removes_its_rows_one_by_one_and_never_resets_it()
    {
        var service = new FakeTaskService();
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());
        foreach (var title in new[] { "Pay rent", "Book flights", "Water plants" })
        {
            var task = await service.CreateTaskAsync(title, Ct);
            if (title != "Pay rent")
            {
                await service.SetCompletedAsync(task.Id, true, Ct);
            }
        }

        await vm.LoadAsync(Ct);
        var completed = vm.Groups.Single(g => g.Kind == TaskGroupKind.Completed);
        var changes = new List<NotifyCollectionChangedAction>();
        completed.CollectionChanged += (_, e) => changes.Add(e.Action);

        completed.IsExpanded = true;
        Assert.Equal(vm.CompletedTasks.Select(r => r.Title), completed.Select(r => r.Title));
        completed.IsExpanded = false;

        Assert.Empty(completed);
        Assert.Equal(
            [
                NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add,
                NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Remove,
            ],
            changes);
    }
}
