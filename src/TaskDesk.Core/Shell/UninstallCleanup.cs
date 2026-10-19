using TaskDesk.Core.Paths;

namespace TaskDesk.Core.Shell;

/// <summary>
/// What uninstalling removes of the app besides its install folder (#90), run by Velopack's uninstall hook before
/// any host exists: the Run key entry, only when it points at this exe, and the app's Windows notification
/// registration. It never touches the data folder. Each step runs even when the other fails.
/// </summary>
public static class UninstallCleanup
{
    /// <exception cref="AggregateException">One or both steps failed; both were attempted.</exception>
    public static void Run(IRunKey runKey, string exePath, Action unregisterNotifications)
    {
        List<Exception> failures = [];

        // Normal mode: the entry is removed only when it points at this exe, whatever the data folder.
        Attempt(() => new AutoStartService(runKey, RuntimeMode.Normal).Apply(false, exePath), failures);
        Attempt(unregisterNotifications, failures);

        if (failures.Count > 0)
        {
            throw new AggregateException("The uninstall cleanup did not finish.", failures);
        }
    }

    private static void Attempt(Action step, List<Exception> failures)
    {
        try
        {
            step();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failures.Add(ex);
        }
    }
}
