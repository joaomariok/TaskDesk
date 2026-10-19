using TaskDesk.Core.Reminders;

namespace TaskDesk.Core.Tests;

public sealed class NotificationExitTests
{
    [Fact]
    public void An_installed_run_only_unregisters()
    {
        var calls = new List<string>();

        NotificationExit.Release(isInstalled: true, () => calls.Add("Unregister"), () => calls.Add("UnregisterAll"));

        Assert.Equal(["Unregister"], calls);
    }

    [Fact]
    public void A_run_that_is_not_installed_removes_its_registration()
    {
        var calls = new List<string>();

        NotificationExit.Release(isInstalled: false, () => calls.Add("Unregister"), () => calls.Add("UnregisterAll"));

        Assert.Equal(["UnregisterAll"], calls);
    }
}
