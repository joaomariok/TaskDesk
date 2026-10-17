namespace TaskDesk.Core.Paths;

/// <summary>
/// Whether the process runs in sandbox mode: a redirected data folder must not touch machine-wide state
/// (the Run key, Windows App SDK notification registrations). The decision lives here so it can be tested.
/// </summary>
public sealed record RuntimeMode(bool IsSandbox)
{
    /// <summary>Name of the environment variable that overrides the automatic decision ("0" turns sandbox off).</summary>
    public const string SandboxVariable = "TASKDESK_SANDBOX";

    /// <summary>The real behaviour: nothing is suppressed.</summary>
    public static RuntimeMode Normal { get; } = new(false);

    /// <summary>Sandbox mode when the data folder is redirected, unless <c>TASKDESK_SANDBOX=0</c> opts out.</summary>
    public static RuntimeMode FromEnvironment() => Resolve(
        Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable),
        Environment.GetEnvironmentVariable(SandboxVariable));

    /// <summary>
    /// Sandbox when <paramref name="dataDirectory"/> is non-blank and <paramref name="sandboxOverride"/> is not
    /// "0" (after trimming). Any other override value, or none, leaves the automatic decision unchanged.
    /// </summary>
    public static RuntimeMode Resolve(string? dataDirectory, string? sandboxOverride) =>
        new(!string.IsNullOrWhiteSpace(dataDirectory) && sandboxOverride?.Trim() != "0");

    public override string ToString() => IsSandbox ? "sandbox" : "normal";
}
