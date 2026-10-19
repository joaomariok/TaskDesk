namespace TaskDesk.Core.Reminders;

/// <summary>
/// What a run does to its Windows notification registration on exit (#90). An installed run only unregisters, so a
/// toast left in Action Center still opens the app; the uninstall hook removes its registration. Any other run (a
/// Debug build, a publish folder) removes its whole registration, which is otherwise left behind per exe path.
/// </summary>
public static class NotificationExit
{
    public static void Release(bool isInstalled, Action unregister, Action unregisterAll)
    {
        if (isInstalled)
        {
            unregister();
        }
        else
        {
            unregisterAll();
        }
    }
}
