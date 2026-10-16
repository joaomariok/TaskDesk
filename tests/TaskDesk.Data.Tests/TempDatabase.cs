using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Paths;

namespace TaskDesk.Data.Tests;

/// <summary>A real SQLite database file in a unique temp folder, removed on dispose.</summary>
public sealed class TempDatabase : IDisposable
{
    private readonly string? _ownedRoot;
    private readonly ServiceProvider _provider;

    public TempDatabase()
        : this(Path.Combine(Path.GetTempPath(), "taskdesk-tests-" + Guid.NewGuid().ToString("N")), owner: true)
    {
    }

    /// <summary>A second app instance over the folder of an existing one; it does not delete the folder.</summary>
    public TempDatabase(IAppPaths existing) : this(existing.DataDirectory, owner: false)
    {
    }

    private TempDatabase(string root, bool owner)
    {
        _ownedRoot = owner ? root : null;
        Paths = AppPaths.ForRoot(root);
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 9, 30, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddTaskDeskData(Paths);
        _provider = services.BuildServiceProvider();
    }

    public IAppPaths Paths { get; }
    public FakeTimeProvider Clock { get; }

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    public async Task<List<T>> QueryAsync<T>(Func<TaskDeskDbContext, IQueryable<T>> query)
    {
        await using var context = await Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync();
        return await query(context).ToListAsync();
    }

    public async Task RenameDefaultListAsync(string name)
    {
        await using var context = await Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync();
        var list = await context.Lists.SingleAsync(l => l.IsDefault);
        list.Name = name;
        await context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        if (_ownedRoot is not null && Directory.Exists(_ownedRoot))
        {
            Directory.Delete(_ownedRoot, recursive: true);
        }
    }
}
