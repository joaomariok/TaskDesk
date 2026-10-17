using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public sealed class StartupFailureTextTests
{
    [Fact]
    public void The_message_says_nothing_was_deleted_and_names_the_log_and_backup_folders()
    {
        var text = StartupFailureText.Describe(
            "TaskDesk could not open its database.", @"C:\Data\logs", @"C:\Data\backups");

        Assert.Equal(
            "TaskDesk could not open its database.\n\nNothing was deleted.\n\n"
            + "Log folder: C:\\Data\\logs\nBackups folder: C:\\Data\\backups",
            text);
    }
}
