namespace TaskDesk.Data;

/// <summary>The database is corrupt or a migration failed. Nothing was deleted; the paths tell the owner where to look.</summary>
public sealed class DatabaseStartupException(string logsDirectory, string backupsDirectory, Exception inner)
    : Exception("TaskDesk could not open its database.", inner)
{
    public string LogsDirectory { get; } = logsDirectory;
    public string BackupsDirectory { get; } = backupsDirectory;
}
