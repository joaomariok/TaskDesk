using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TaskDesk.Core.Backups;
using TaskDesk.Core.Paths;

namespace TaskDesk.Data;

internal sealed class BackupService(IAppPaths paths, TimeProvider clock, ILogger<BackupService> logger) : IBackupService
{
    public async Task<string> BackUpNowAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        var target = Path.Combine(paths.BackupsDirectory, BackupNaming.FileName(clock.GetLocalNow().DateTime));
        var temp = target + ".tmp";

        try
        {
            await Task.Run(() => Copy(temp), cancellationToken);
            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }

        logger.LogInformation("Database backed up to {BackupFile}", target);
        Prune();
        return target;
    }

    public async Task<string?> BackUpIfDueAsync(CancellationToken cancellationToken = default)
    {
        var today = clock.GetLocalNow().Date;
        var hasToday = Directory.Exists(paths.BackupsDirectory)
            && Directory.EnumerateFiles(paths.BackupsDirectory)
                .Any(f => BackupNaming.TryParse(Path.GetFileName(f), out var time) && time.Date >= today);
        return hasToday ? null : await BackUpNowAsync(cancellationToken);
    }

    private void Copy(string destination)
    {
        // Pooling=False so both files are released as soon as the connections are disposed.
        using var source = new SqliteConnection($"Data Source={paths.DatabaseFile};Mode=ReadOnly;Pooling=False");
        using var copy = new SqliteConnection($"Data Source={destination};Pooling=False");
        source.Open();
        copy.Open();
        source.BackupDatabase(copy);
    }

    private void Prune()
    {
        var names = Directory.EnumerateFiles(paths.BackupsDirectory).Select(f => Path.GetFileName(f)!);
        foreach (var name in BackupNaming.SelectExpired(names))
        {
            try
            {
                File.Delete(Path.Combine(paths.BackupsDirectory, name));
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not delete the old backup {BackupFile}", name);
            }
        }
    }
}
