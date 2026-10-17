namespace TaskDesk.Core.Paths;

/// <summary>The single source of every file and folder TaskDesk reads or writes.</summary>
public interface IAppPaths
{
    string DataDirectory { get; }
    string DatabaseFile { get; }
    string SettingsFile { get; }
    string BackupsDirectory { get; }
    string LogsDirectory { get; }

    /// <summary>Creates the data, backups and logs folders when missing.</summary>
    void EnsureDirectories();
}

public sealed class AppPaths : IAppPaths
{
    private AppPaths(string root) => DataDirectory = root;

    public string DataDirectory { get; }
    public string DatabaseFile => Path.Combine(DataDirectory, "taskdesk.db");
    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public string BackupsDirectory => Path.Combine(DataDirectory, "backups");
    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>Name of the environment variable that redirects the data folder (for measuring and testing).</summary>
    public const string DataDirectoryVariable = "TASKDESK_DATA_DIR";

    /// <summary>
    /// Paths under <c>%LOCALAPPDATA%\TaskDesk</c>, or under the folder named by the
    /// <c>TASKDESK_DATA_DIR</c> environment variable when it is set and not blank.
    /// </summary>
    public static IAppPaths Default() => Resolve(Environment.GetEnvironmentVariable(DataDirectoryVariable));

    /// <summary>The paths for an optional override folder; a null or blank override means the default location.</summary>
    public static IAppPaths Resolve(string? overrideRoot) => ForRoot(string.IsNullOrWhiteSpace(overrideRoot)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskDesk")
        : overrideRoot);

    /// <summary>Paths under an arbitrary root; tests use this to redirect to a temp folder.</summary>
    public static IAppPaths ForRoot(string root) => new AppPaths(root);

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
