using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TaskDesk.Core.Backups;

namespace TaskDesk.Data.Tests;

public sealed class BackupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static long Scalar(string file, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string[] BackupNames(TempDatabase db) =>
        [.. Directory.GetFiles(db.Paths.BackupsDirectory).Select(f => Path.GetFileName(f)!).Order()];

    [Fact]
    public async Task A_backup_is_a_readable_copy_named_by_timestamp()
    {
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        await db.AddTaskAsync((await db.QueryAsync(c => c.Lists)).Single().Id, "Keep me", completed: false);

        var path = await db.Get<IBackupService>().BackUpNowAsync(Ct);

        Assert.Equal(Path.Combine(db.Paths.BackupsDirectory, "taskdesk-20261016-093000.db"), path);
        Assert.Equal(1, Scalar(path, "SELECT COUNT(*) FROM Tasks WHERE Title = 'Keep me'"));
    }

    [Fact]
    public async Task Only_the_newest_ten_backups_are_kept()
    {
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        var service = db.Get<IBackupService>();

        for (var i = 0; i < 12; i++)
        {
            db.Clock.Advance(TimeSpan.FromMinutes(1));
            await service.BackUpNowAsync(Ct);
        }

        var names = BackupNames(db);
        Assert.Equal(10, names.Length);
        Assert.Equal("taskdesk-20261016-094200.db", names[^1]);
        Assert.Equal("taskdesk-20261016-093300.db", names[0]);
    }

    [Fact]
    public async Task The_daily_backup_runs_once_per_day()
    {
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        var service = db.Get<IBackupService>();
        Assert.Single(BackupNames(db));

        Assert.Null(await service.BackUpIfDueAsync(Ct));

        db.Clock.Advance(TimeSpan.FromDays(1));
        Assert.NotNull(await service.BackUpIfDueAsync(Ct));
        Assert.Null(await service.BackUpIfDueAsync(Ct));
        Assert.Equal(2, BackupNames(db).Length);
    }

    [Fact]
    public async Task Pending_migrations_on_an_existing_database_are_preceded_by_a_backup()
    {
        using var db = new TempDatabase();
        db.Paths.EnsureDirectories();
        await using (var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync(Ct))
        {
            var first = context.Database.GetMigrations().First();
            await context.Database.GetService<IMigrator>().MigrateAsync(first, Ct);
        }

        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);

        var backup = Assert.Single(Directory.GetFiles(db.Paths.BackupsDirectory));
        Assert.Equal(1, Scalar(backup, "SELECT COUNT(*) FROM __EFMigrationsHistory"));
        Assert.True(Scalar(db.Paths.DatabaseFile, "SELECT COUNT(*) FROM __EFMigrationsHistory") > 1);
    }

    [Fact]
    public async Task A_corrupt_database_raises_a_startup_error_and_nothing_is_deleted()
    {
        using var db = new TempDatabase();
        db.Paths.EnsureDirectories();
        var garbage = "this is not a sqlite database, just text"u8.ToArray();
        await File.WriteAllBytesAsync(db.Paths.DatabaseFile, garbage, Ct);
        var oldBackup = Path.Combine(db.Paths.BackupsDirectory, "taskdesk-20260101-000000.db");
        await File.WriteAllTextAsync(oldBackup, "old", Ct);

        var error = await Assert.ThrowsAsync<DatabaseStartupException>(
            () => db.Get<IDatabaseInitializer>().InitializeAsync(Ct));

        Assert.Equal(db.Paths.LogsDirectory, error.LogsDirectory);
        Assert.Equal(db.Paths.BackupsDirectory, error.BackupsDirectory);
        Assert.Equal(garbage, await File.ReadAllBytesAsync(db.Paths.DatabaseFile, Ct));
        Assert.True(File.Exists(oldBackup));
    }
}
