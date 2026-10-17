namespace TaskDesk.UiTests;

/// <summary>
/// The HideToTray close behaviour in a real process (MANUAL-TESTS: Tray and window): closing hides the window and
/// the app keeps running; a later launch shows it again. Framework-neutral, like <see cref="WindowLifecycleTests"/>.
/// </summary>
public sealed class CloseToTrayTests : IDisposable
{
    private readonly AppSession _app = AppSession.Start(closeBehavior: "HideToTray");

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Closing_the_window_hides_it_to_the_tray_and_a_second_launch_shows_it_again()
    {
        var handle = _app.Window.Properties.NativeWindowHandle.Value;

        _app.Window.Close();

        AppSession.WaitUntil(() => !AppSession.IsWindowShown(handle), () => "The window is still shown after Close.");
        Assert.True(_app.IsRunning, "TaskDesk exited instead of hiding to the tray.");

        _app.LaunchSecondInstance();

        AppSession.WaitUntil(() => AppSession.IsWindowShown(handle), () => "The second launch did not show the hidden window.");
    }
}
