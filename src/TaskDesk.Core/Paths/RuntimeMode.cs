namespace TaskDesk.Core.Paths;

/// <summary>
/// What the process may do to machine-wide state. A redirected data folder (a trial or test run) must never touch
/// the Run key; <c>TASKDESK_SANDBOX=0</c> only opts back in to Windows App SDK notification registrations.
/// The decision lives here so it can be tested.
/// </summary>
public sealed record RuntimeMode(bool ManagesAutoStart, bool RegistersNotifications)
{
    /// <summary>Name of the environment variable that re-enables toast registration ("0") for a redirected data folder.</summary>
    public const string SandboxVariable = "TASKDESK_SANDBOX";

    /// <summary>The real behaviour: nothing is suppressed.</summary>
    public static RuntimeMode Normal { get; } = new(true, true);

    /// <summary>Resolves the mode from <c>TASKDESK_DATA_DIR</c> and <c>TASKDESK_SANDBOX</c>.</summary>
    public static RuntimeMode FromEnvironment() => Resolve(
        Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable),
        Environment.GetEnvironmentVariable(SandboxVariable));

    /// <summary>
    /// A non-blank <paramref name="dataDirectory"/> stops the Run key from being managed, whatever the override.
    /// It also stops notification registration unless <paramref name="sandboxOverride"/> is "0" (after trimming).
    /// </summary>
    public static RuntimeMode Resolve(string? dataDirectory, string? sandboxOverride)
    {
        var redirected = !string.IsNullOrWhiteSpace(dataDirectory);
        return redirected ? new RuntimeMode(false, sandboxOverride?.Trim() == "0") : Normal;
    }

    public override string ToString()
    {
        if (ManagesAutoStart && RegistersNotifications)
        {
            return "normal";
        }

        var notifications = RegistersNotifications ? "notifications registered" : "notifications not registered";
        var name = RegistersNotifications ? "redirected data folder" : "sandbox";
        return $"{name} (auto-start untouched, {notifications})";
    }
}
