using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;

namespace TaskDesk.Presentation.Tests;

public class MainViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_view_model_needs_a_clock()
    {
        var service = new FakeTaskService();

        Assert.Throws<ArgumentNullException>(() => new MainViewModel(service, new FakeTagService(service), null!));
    }

    [Fact]
    public async Task Load_shows_the_existing_Tasks_in_order()
    {
        var service = new FakeTaskService();
        await service.CreateTaskAsync("one", Ct);
        await service.CreateTaskAsync("two", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());

        await vm.LoadAsync(Ct);

        Assert.Equal(["one", "two"], vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Quick_add_creates_the_Task_clears_the_box_and_shows_it()
    {
        var service = new FakeTaskService();
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider()) { QuickAddTitle = "Buy milk" };

        await vm.AddTaskCommand.ExecuteAsync(null);

        Assert.Equal(["Buy milk"], service.Created);
        Assert.Equal(["Buy milk"], vm.Tasks.Select(t => t.Title));
        Assert.Equal("", vm.QuickAddTitle);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Quick_add_rejects_an_empty_title(string title)
    {
        var service = new FakeTaskService();
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider()) { QuickAddTitle = title };

        Assert.False(vm.AddTaskCommand.CanExecute(null));
        await vm.AddTaskCommand.ExecuteAsync(null);

        Assert.Empty(service.Created);
        Assert.Empty(vm.Tasks);
    }

    [Fact]
    public void Quick_add_command_becomes_executable_once_a_title_is_typed()
    {
        var vm = new MainViewModel(new FakeTaskService(), new FakeTagService(new FakeTaskService()), new FakeTimeProvider());
        var changes = 0;
        vm.AddTaskCommand.CanExecuteChanged += (_, _) => changes++;

        vm.QuickAddTitle = "x";

        Assert.True(vm.AddTaskCommand.CanExecute(null));
        Assert.True(changes > 0);
    }

    [Fact]
    public async Task Moving_a_Task_reorders_the_list_and_asks_the_service_to_persist_it()
    {
        var service = new FakeTaskService();
        await service.CreateTaskAsync("a", Ct);
        await service.CreateTaskAsync("b", Ct);
        await service.CreateTaskAsync("c", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());
        await vm.LoadAsync(Ct);
        var a = vm.Tasks[0];

        await vm.MoveTaskAsync(0, 2, Ct);

        Assert.Equal(["b", "c", "a"], vm.Tasks.Select(t => t.Title));
        Assert.Equal([(a.Id, 2)], service.Moves);
    }

    [Fact]
    public async Task Moving_is_ignored_when_reordering_is_disabled()
    {
        var service = new FakeTaskService();
        await service.CreateTaskAsync("a", Ct);
        await service.CreateTaskAsync("b", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider()) { IsReorderEnabled = false };
        await vm.LoadAsync(Ct);

        await vm.MoveTaskAsync(0, 1, Ct);

        Assert.Equal(["a", "b"], vm.Tasks.Select(t => t.Title));
        Assert.Empty(service.Moves);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 1)]
    [InlineData(0, 2)]
    public async Task Moving_to_the_same_or_an_invalid_position_does_nothing(int from, int to)
    {
        var service = new FakeTaskService();
        await service.CreateTaskAsync("a", Ct);
        await service.CreateTaskAsync("b", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());
        await vm.LoadAsync(Ct);

        await vm.MoveTaskAsync(from, to, Ct);

        Assert.Equal(["a", "b"], vm.Tasks.Select(t => t.Title));
        Assert.Empty(service.Moves);
    }

    [Theory]
    [InlineData(0, 0, "a,b,c,d", false)] // a's own gap, above it
    [InlineData(0, 1, "a,b,c,d", false)] // a's own gap, below it
    [InlineData(0, 2, "b,a,c,d", true)] // between b and c
    [InlineData(0, 4, "b,c,d,a", true)] // after the last row
    [InlineData(0, 9, "b,c,d,a", true)] // past the rows means the end
    [InlineData(2, 0, "c,a,b,d", true)] // before the first row
    [InlineData(2, 1, "a,c,b,d", true)] // between a and b
    [InlineData(2, 2, "a,b,c,d", false)] // c's own gap, above it
    [InlineData(2, 3, "a,b,c,d", false)] // c's own gap, below it
    [InlineData(2, 4, "a,b,d,c", true)] // after the last row
    [InlineData(3, 4, "a,b,c,d", false)] // d is already last
    [InlineData(3, 0, "d,a,b,c", true)] // last row to the front
    [InlineData(1, -1, "a,b,c,d", false)] // no such gap
    public async Task Moving_a_Task_into_a_gap_saves_the_order_and_its_own_gap_changes_nothing(
        int dragged, int gap, string expected, bool moves)
    {
        var service = new FakeTaskService();
        foreach (var title in new[] { "a", "b", "c", "d" })
        {
            await service.CreateTaskAsync(title, Ct);
        }

        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());
        await vm.LoadAsync(Ct);

        await vm.MoveTaskToGapAsync(vm.Tasks[dragged], gap, Ct);

        Assert.Equal(expected.Split(','), vm.Tasks.Select(t => t.Title));
        Assert.Equal(moves ? 1 : 0, service.Moves.Count);
        var saved = service.All.Select(t => t.Title);
        Assert.Equal(expected.Split(','), saved);
    }

    [Fact]
    public async Task Moving_into_a_gap_is_ignored_when_reordering_is_disabled()
    {
        var service = new FakeTaskService();
        await service.CreateTaskAsync("a", Ct);
        await service.CreateTaskAsync("b", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider()) { IsReorderEnabled = false };
        await vm.LoadAsync(Ct);

        await vm.MoveTaskToGapAsync(vm.Tasks[0], 2, Ct);

        Assert.Equal(["a", "b"], vm.Tasks.Select(t => t.Title));
        Assert.Empty(service.Moves);
    }

    [Fact]
    public async Task Adding_a_Tag_to_the_selected_Task_shows_it_on_the_row_and_clears_the_box()
    {
        var service = new FakeTaskService();
        var task = await service.CreateTaskAsync("one", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks.Single();
        vm.NewTagName = "work";

        await vm.AddTagCommand.ExecuteAsync(null);

        Assert.Equal(["work"], vm.Tasks.Single().Tags.Select(t => t.Name));
        Assert.Equal(["work"], vm.Tags.Select(t => t.Name));
        Assert.Equal("", vm.NewTagName);
        Assert.Equal(task.Id, vm.SelectedTask?.Id);
    }

    [Theory]
    [InlineData("two words")]
    [InlineData("#work")]
    [InlineData("")]
    public async Task Adding_an_invalid_Tag_name_is_not_possible(string name)
    {
        var service = new FakeTaskService();
        await service.CreateTaskAsync("one", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks.Single();
        vm.NewTagName = name;

        Assert.False(vm.AddTagCommand.CanExecute(null));
    }

    [Fact]
    public async Task Removing_a_Tag_from_the_selected_Task_keeps_the_Tag_in_navigation()
    {
        var service = new FakeTaskService();
        var tags = new FakeTagService(service);
        var task = await service.CreateTaskAsync("one", Ct);
        var tag = await tags.AddTagToTaskAsync(task.Id, "work", Ct);
        var vm = new MainViewModel(service, tags, new FakeTimeProvider());
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks.Single();

        await vm.RemoveTagCommand.ExecuteAsync(tag);

        Assert.Empty(vm.Tasks.Single().Tags);
        Assert.Single(vm.Tags);
    }

    [Fact]
    public async Task Filtering_by_a_Tag_shows_only_its_Tasks_and_clearing_restores_the_list()
    {
        var service = new FakeTaskService();
        var tags = new FakeTagService(service);
        var tagged = await service.CreateTaskAsync("tagged", Ct);
        await service.CreateTaskAsync("plain", Ct);
        await tags.AddTagToTaskAsync(tagged.Id, "work", Ct);
        var vm = new MainViewModel(service, tags, new FakeTimeProvider());
        await vm.LoadAsync(Ct);

        await vm.FilterByTagAsync(vm.Tags.Single(), Ct);
        Assert.Equal(["tagged"], vm.Tasks.Select(t => t.Title));

        await vm.FilterByTagAsync(null, Ct);
        Assert.Equal(["tagged", "plain"], vm.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Deleting_the_filtered_Tag_drops_the_filter_and_shows_all_Tasks_again()
    {
        var service = new FakeTaskService();
        var tags = new FakeTagService(service);
        var tagged = await service.CreateTaskAsync("tagged", Ct);
        await service.CreateTaskAsync("plain", Ct);
        await tags.AddTagToTaskAsync(tagged.Id, "work", Ct);
        var vm = new MainViewModel(service, tags, new FakeTimeProvider());
        await vm.LoadAsync(Ct);
        await vm.FilterByTagAsync(vm.Tags.Single(), Ct);

        await vm.DeleteTagAsync(vm.Tags.Single(), Ct);

        Assert.Null(vm.TagFilter);
        Assert.Empty(vm.Tags);
        Assert.Equal(2, vm.Tasks.Count);
    }

    [Fact]
    public async Task Adding_completing_un_completing_deleting_and_restoring_a_Task_signal_open_count_changes()
    {
        var service = new FakeTaskService();
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());
        await vm.LoadAsync(Ct);
        var signals = 0;
        vm.OpenTaskCountsChanged += (_, _) => signals++;

        vm.QuickAddTitle = "a";
        await vm.AddTaskCommand.ExecuteAsync(null);
        Assert.Equal(1, signals);

        await vm.ToggleCompleteCommand.ExecuteAsync(vm.Tasks.Single());
        Assert.Equal(2, signals);

        await vm.ToggleCompleteCommand.ExecuteAsync(vm.CompletedTasks.Single());
        Assert.Equal(3, signals);

        await vm.DeleteCommand.ExecuteAsync(vm.Tasks.Single());
        Assert.Equal(4, signals);

        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(5, signals);
    }

    [Fact]
    public async Task A_completed_Task_keeps_its_Tag_chips_and_shows_in_the_Completed_group_of_a_Tag_filter()
    {
        var service = new FakeTaskService();
        var tags = new FakeTagService(service);
        var task = await service.CreateTaskAsync("tagged", Ct);
        await tags.AddTagToTaskAsync(task.Id, "work", Ct);
        var vm = new MainViewModel(service, tags, new FakeTimeProvider());
        await vm.LoadAsync(Ct);

        await vm.ToggleCompleteCommand.ExecuteAsync(vm.Tasks.Single());
        Assert.Equal(["work"], vm.CompletedTasks.Single().Tags.Select(t => t.Name));

        await vm.FilterByTagAsync(vm.Tags.Single(), Ct);

        Assert.Empty(vm.Tasks);
        Assert.Equal(["tagged"], vm.CompletedTasks.Select(t => t.Title));
        Assert.Equal(["work"], vm.CompletedTasks.Single().Tags.Select(t => t.Name));
    }

    [Fact]
    public async Task Undo_after_delete_shows_the_restored_Task_with_its_Tag_chips()
    {
        var service = new FakeTaskService();
        var tags = new FakeTagService(service);
        var task = await service.CreateTaskAsync("tagged", Ct);
        await tags.AddTagToTaskAsync(task.Id, "work", Ct);
        var vm = new MainViewModel(service, tags, new FakeTimeProvider());
        await vm.LoadAsync(Ct);

        await vm.DeleteCommand.ExecuteAsync(vm.Tasks.Single());
        await vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal(["work"], vm.Tasks.Single().Tags.Select(t => t.Name));
    }

    [Fact]
    public async Task Adding_a_Tag_keeps_the_selected_Task_and_a_closed_detail_pane_closed()
    {
        var service = new FakeTaskService();
        await service.CreateTaskAsync("one", Ct);
        var vm = new MainViewModel(service, new FakeTagService(service), new FakeTimeProvider());
        await vm.LoadAsync(Ct);
        vm.SelectedTask = vm.Tasks.Single();
        vm.CloseDetailPaneCommand.Execute(null);
        vm.NewTagName = "work";

        await vm.AddTagCommand.ExecuteAsync(null);

        Assert.Same(vm.Tasks.Single(), vm.SelectedTask);
        Assert.False(vm.IsDetailPaneOpen);
    }

    internal sealed class FakeTagService(FakeTaskService tasks) : ITagService
    {
        private readonly List<Tag> _tags = [];
        private readonly List<(Guid TaskId, Guid TagId)> _links = [];

        public Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Tag>>(_tags.OrderBy(t => t.NormalizedName).ToList());

        public Task<Tag> CreateTagAsync(string name, CancellationToken cancellationToken = default)
        {
            var tag = new Tag { Id = Guid.NewGuid(), Name = TagName.Validate(name), NormalizedName = TagName.Normalize(name) };
            _tags.Add(tag);
            return Task.FromResult(tag);
        }

        public async Task<Tag> AddTagToTaskAsync(Guid taskId, string name, CancellationToken cancellationToken = default)
        {
            TagName.Validate(name);
            var tag = _tags.FirstOrDefault(t => t.NormalizedName == TagName.Normalize(name))
                ?? await CreateTagAsync(name, cancellationToken);
            if (!_links.Contains((taskId, tag.Id)))
            {
                _links.Add((taskId, tag.Id));
            }

            Refresh();
            return tag;
        }

        public Task RemoveTagFromTaskAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default)
        {
            _links.Remove((taskId, tagId));
            Refresh();
            return Task.CompletedTask;
        }

        public Task DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default)
        {
            _tags.RemoveAll(t => t.Id == tagId);
            _links.RemoveAll(l => l.TagId == tagId);
            Refresh();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TaskItem>> ListTasksByTagAsync(Guid tagId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>(
                tasks.All.Where(t => _links.Contains((t.Id, tagId))).ToList());

        private void Refresh()
        {
            foreach (var task in tasks.All)
            {
                task.Tags = _links.Where(l => l.TaskId == task.Id)
                    .Select(l => _tags.Single(t => t.Id == l.TagId)).ToList();
            }
        }
    }

    internal sealed class FakeTaskService : ITaskService, ITaskCreator
    {
        private readonly List<TaskItem> _tasks = [];
        public List<string> Created { get; } = [];
        public Exception? DeleteFailure { get; init; }
        public IReadOnlyList<TaskItem> All => _tasks;
        public List<(Guid TaskId, int NewIndex)> Moves { get; } = [];

        public Task MoveTaskAsync(Guid taskId, int newIndex, CancellationToken cancellationToken = default)
        {
            Moves.Add((taskId, newIndex));
            var task = _tasks.Single(t => t.Id == taskId);
            _tasks.Remove(task);
            _tasks.Insert(newIndex, task);
            return Task.CompletedTask;
        }

        public List<(Guid TaskId, string Title, string? Notes, Priority Priority)> Updates { get; } = [];
        public List<(Guid TaskId, bool IsCompleted)> Completions { get; } = [];
        public List<Guid> Deleted { get; } = [];
        public List<Guid> Restored { get; } = [];
        private double _nextOrder = 1;

        public Task<TaskItem> SetCompletedAsync(Guid taskId, bool isCompleted, CancellationToken cancellationToken = default)
        {
            Completions.Add((taskId, isCompleted));
            var index = _tasks.FindIndex(t => t.Id == taskId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Task {taskId} does not exist.");
            }

            var completedUtc = new DateTime(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc).AddMinutes(Completions.Count);
            _tasks[index] = Copy(_tasks[index], t =>
            {
                t.IsCompleted = isCompleted;
                t.CompletedUtc = isCompleted ? completedUtc : null;
            });

            // Emulates the at-most-once next Occurrence (a simple +1 day; the real rules live in Core).
            if (isCompleted && _tasks[index] is { Recurrence: not null, NextOccurrenceId: null } current)
            {
                var next = Copy(current, t =>
                {
                    t.Id = Guid.NewGuid();
                    t.IsCompleted = false;
                    t.CompletedUtc = null;
                    t.DueDate = current.DueDate?.AddDays(1);
                    t.SortOrder = current.SortOrder + 0.5;
                });
                _tasks.Insert(index + 1, next);
                _tasks[index] = Copy(current, t => t.NextOccurrenceId = next.Id);
            }

            return Task.FromResult(_tasks[index]);
        }

        /// <summary>Puts a Task straight into the fake store (used by fakes of other services).</summary>
        internal void Put(TaskItem task)
        {
            var index = _tasks.FindIndex(t => t.Id == task.Id);
            if (index >= 0)
            {
                _tasks[index] = task;
            }
            else
            {
                _tasks.Add(task);
                _tasks.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            }
        }

        internal void Remove(Guid taskId) => _tasks.RemoveAll(t => t.Id == taskId);

        internal static TaskItem With(TaskItem source, Action<TaskItem> change) => Copy(source, change);

        public Task<TaskItem> UpdateTaskAsync(
            Guid taskId, string title, string? notes, Priority priority, CancellationToken cancellationToken = default)
        {
            Updates.Add((taskId, title, notes, priority));
            var index = _tasks.FindIndex(t => t.Id == taskId);
            _tasks[index] = Copy(_tasks[index], t => { t.Title = title; t.Notes = notes; t.Priority = priority; });
            return Task.FromResult(_tasks[index]);
        }

        public Task<DeletedTask> DeleteTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            if (DeleteFailure is { } failure)
            {
                throw failure;
            }

            Deleted.Add(taskId);
            var task = _tasks.Single(t => t.Id == taskId);
            _tasks.Remove(task);
            return Task.FromResult(new DeletedTask(task, task.Tags.ConvertAll(t => t.Id), [.. task.Subtasks]));
        }

        public Task<TaskItem> RestoreTaskAsync(DeletedTask deleted, CancellationToken cancellationToken = default)
        {
            Restored.Add(deleted.Task.Id);
            _tasks.Add(deleted.Task);
            _tasks.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            return Task.FromResult(deleted.Task);
        }

        private static TaskItem Copy(TaskItem source, Action<TaskItem> change)
        {
            var copy = new TaskItem
            {
                Id = source.Id,
                ListId = source.ListId,
                Title = source.Title,
                Notes = source.Notes,
                Priority = source.Priority,
                IsCompleted = source.IsCompleted,
                CompletedUtc = source.CompletedUtc,
                DueDate = source.DueDate,
                DueTime = source.DueTime,
                NotifyAtDue = source.NotifyAtDue,
                Reminders =
                [
                    .. source.Reminders.Select(r => new TaskDesk.Core.Reminders.Reminder
                    {
                        Id = r.Id,
                        TaskId = r.TaskId,
                        Kind = r.Kind,
                        AtUtc = r.AtUtc,
                        Amount = r.Amount,
                        Unit = r.Unit,
                    }),
                ],
                SnoozedUntilUtc = source.SnoozedUntilUtc,
                NotifiedThroughUtc = source.NotifiedThroughUtc,
                Recurrence = source.Recurrence,
                SeriesId = source.SeriesId,
                NextOccurrenceId = source.NextOccurrenceId,
                SortOrder = source.SortOrder,
                CreatedUtc = source.CreatedUtc,
                UpdatedUtc = source.UpdatedUtc,
                Tags = source.Tags,
                Subtasks = source.Subtasks,
            };
            change(copy);
            return copy;
        }

        public List<Guid> DeletedSubtasks { get; } = [];
        public List<Guid> RestoredSubtasks { get; } = [];

        private static Subtask Clone(Subtask s) => new()
        {
            Id = s.Id,
            TaskId = s.TaskId,
            Title = s.Title,
            IsCompleted = s.IsCompleted,
            SortOrder = s.SortOrder,
        };

        private TaskItem OwnerOf(Guid subtaskId)
            => _tasks.Single(t => t.Subtasks.Exists(s => s.Id == subtaskId));

        public Task<Subtask> AddSubtaskAsync(Guid taskId, string title, CancellationToken cancellationToken = default)
        {
            var task = _tasks.Single(t => t.Id == taskId);
            var subtask = new Subtask
            {
                Id = Guid.NewGuid(),
                TaskId = taskId,
                Title = TaskTitle.Normalize(title),
                SortOrder = task.Subtasks.Count == 0 ? 1 : task.Subtasks.Max(s => s.SortOrder) + 1,
            };
            task.Subtasks.Add(subtask);
            return Task.FromResult(Clone(subtask));
        }

        public Task<Subtask> RenameSubtaskAsync(Guid subtaskId, string title, CancellationToken cancellationToken = default)
        {
            var subtask = OwnerOf(subtaskId).Subtasks.Single(s => s.Id == subtaskId);
            subtask.Title = TaskTitle.Normalize(title);
            return Task.FromResult(Clone(subtask));
        }

        public Task<Subtask> SetSubtaskCompletedAsync(
            Guid subtaskId, bool isCompleted, CancellationToken cancellationToken = default)
        {
            var subtask = OwnerOf(subtaskId).Subtasks.Single(s => s.Id == subtaskId);
            subtask.IsCompleted = isCompleted;
            return Task.FromResult(Clone(subtask));
        }

        public Task<IReadOnlyList<Subtask>> MoveSubtaskAsync(
            Guid subtaskId, int newIndex, CancellationToken cancellationToken = default)
        {
            var list = OwnerOf(subtaskId).Subtasks;
            var moved = list.Single(s => s.Id == subtaskId);
            list.Remove(moved);
            list.Insert(newIndex, moved);
            for (var i = 0; i < list.Count; i++)
            {
                list[i].SortOrder = i + 1;
            }

            return Task.FromResult<IReadOnlyList<Subtask>>([.. list.Select(Clone)]);
        }

        public Task<Subtask> DeleteSubtaskAsync(Guid subtaskId, CancellationToken cancellationToken = default)
        {
            DeletedSubtasks.Add(subtaskId);
            var list = OwnerOf(subtaskId).Subtasks;
            var subtask = list.Single(s => s.Id == subtaskId);
            list.Remove(subtask);
            return Task.FromResult(subtask);
        }

        public Task<Subtask> RestoreSubtaskAsync(Subtask deleted, CancellationToken cancellationToken = default)
        {
            RestoredSubtasks.Add(deleted.Id);
            var list = _tasks.Single(t => t.Id == deleted.TaskId).Subtasks;
            list.Add(deleted);
            list.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            return Task.FromResult(deleted);
        }

        public Task<TaskItem> CreateTaskAsync(string title, CancellationToken cancellationToken = default)
        {
            var task = new TaskItem { Id = Guid.NewGuid(), Title = TaskTitle.Normalize(title), SortOrder = _nextOrder++ };
            _tasks.Add(task);
            Created.Add(task.Title);
            return Task.FromResult(task);
        }

        // Tasks created in a chosen List; every other Task counts as the Default List's.
        private readonly HashSet<Guid> _inChosenList = [];

        public List<(string Title, Guid ListId)> CreatedInList { get; } = [];

        public Task<IReadOnlyList<TaskItem>> ListDefaultListTasksAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>(_tasks.Where(t => !_inChosenList.Contains(t.Id)).ToList());

        public Task<IReadOnlyList<TaskItem>> ListTasksInListAsync(Guid listId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>(_tasks.Where(t => t.ListId == listId).ToList());

        public Task<TaskItem> CreateTaskInListAsync(string title, Guid listId, CancellationToken cancellationToken = default)
            => CreateTaskInListAsync(title, listId, null, cancellationToken);

        public Task<TaskItem> CreateTaskInListAsync(
            string title, Guid listId, DateOnly? dueDate, CancellationToken cancellationToken = default)
        {
            var task = new TaskItem
            {
                Id = Guid.NewGuid(),
                ListId = listId,
                Title = TaskTitle.Normalize(title),
                DueDate = dueDate,
                SortOrder = _nextOrder++,
            };
            _tasks.Add(task);
            _inChosenList.Add(task.Id);
            CreatedInList.Add((task.Title, listId));
            return Task.FromResult(task);
        }
    }
}
