namespace TaskDesk.Core.Backups;

public interface IBackupService
{
    /// <summary>Takes an online backup of the database now and prunes old ones. Returns the backup file path.</summary>
    Task<string> BackUpNowAsync(CancellationToken cancellationToken = default);

    /// <summary>Backs up unless a backup already exists for today (local date). Returns the path, or null when skipped.</summary>
    Task<string?> BackUpIfDueAsync(CancellationToken cancellationToken = default);
}
