using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

/// <summary>
/// Raises <see cref="TasksChanged"/> after every successful save that added, edited or removed a Task or one of
/// its Reminders, whichever service made it. Bulk updates (ExecuteUpdate) bypass it.
/// </summary>
internal sealed class TaskChangeInterceptor : SaveChangesInterceptor, ITaskChangeNotifier
{
    private static readonly object Marker = new();

    // Contexts whose save in progress changes Tasks. Weak, so a context that fails to save is not kept alive.
    private readonly ConditionalWeakTable<DbContext, object> _changing = [];

    public event Action? TasksChanged;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Note(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Note(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        RaiseIfNoted(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        RaiseIfNoted(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData.Context);

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    private void Note(DbContext? context)
    {
        if (context is not null && context.ChangeTracker.Entries()
                .Any(e => e.Entity is TaskItem or Reminder
                    && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            _changing.AddOrUpdate(context, Marker);
        }
    }

    private void RaiseIfNoted(DbContext? context)
    {
        if (context is not null && _changing.Remove(context))
        {
            TasksChanged?.Invoke();
        }
    }

    private void Forget(DbContext? context)
    {
        if (context is not null)
        {
            _changing.Remove(context);
        }
    }
}
