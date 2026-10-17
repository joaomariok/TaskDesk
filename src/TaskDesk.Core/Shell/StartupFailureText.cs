namespace TaskDesk.Core.Shell;

/// <summary>The text of the dialog shown when the database cannot be opened at start-up.</summary>
public static class StartupFailureText
{
    /// <summary>The dialog's title.</summary>
    public const string Title = "TaskDesk";

    /// <param name="message">What went wrong.</param>
    /// <param name="logsDirectory">The folder the log explaining it is in.</param>
    /// <param name="backupsDirectory">The folder the backups are in.</param>
    public static string Describe(string message, string logsDirectory, string backupsDirectory) =>
        $"{message}\n\nNothing was deleted.\n\nLog folder: {logsDirectory}\nBackups folder: {backupsDirectory}";
}
