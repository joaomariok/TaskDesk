namespace TaskDesk.Core.Import;

/// <summary>How an import combines the file with the data already there (SPEC 9.3).</summary>
public enum ImportMode
{
    /// <summary>Add what is new, skip ids that already exist; the local Default List stays the default.</summary>
    Merge,

    /// <summary>Back up, delete everything, then import; the file's default List becomes the Default List.</summary>
    Replace,
}

/// <summary>How many rows of one kind were added and how many were skipped (already there or matched).</summary>
public sealed record ImportCounts(int Added, int Skipped);

/// <param name="Mode">The mode that ran.</param>
/// <param name="Lists">Lists added; skipped counts Lists matched by id or name.</param>
/// <param name="Tags">Tags added; skipped counts Tags matched by name or id.</param>
/// <param name="Tasks">Tasks added (with their Subtasks); skipped counts ids that already existed.</param>
/// <param name="BackupPath">The backup taken before a Replace; null for a Merge.</param>
public sealed record ImportResult(
    ImportMode Mode, ImportCounts Lists, ImportCounts Tags, ImportCounts Tasks, string? BackupPath);

/// <summary>Use case for importing a JSON export (SPEC 9.3).</summary>
public interface IImportService
{
    /// <summary>
    /// Validates the whole file, then imports it in one transaction: either everything is imported or nothing.
    /// Reminders whose firing instant has already passed are imported as already fired.
    /// </summary>
    /// <param name="path">The JSON export to read.</param>
    /// <param name="mode">Merge or Replace.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>What was added and skipped.</returns>
    /// <exception cref="ImportException">The file is not a valid export; the database was not touched.</exception>
    Task<ImportResult> ImportJsonAsync(string path, ImportMode mode, CancellationToken cancellationToken = default);
}
