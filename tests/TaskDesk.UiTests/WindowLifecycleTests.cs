using FlaUI.Core.Definitions;

namespace TaskDesk.UiTests;

/// <summary>
/// The main window's lifecycle in a real process (MANUAL-TESTS: Tray and window): close exits with the Exit close
/// behaviour, a restart restores the size and position, and a second launch brings the running window forward.
/// </summary>
public sealed class WindowLifecycleTests : IDisposable
{
    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Closing_the_window_with_the_Exit_close_behaviour_ends_the_process()
    {
        _app.CloseAndWaitForExit();
    }

    [Fact]
    public void A_restart_restores_the_window_size_and_position()
    {
        _app.ResizeTo(1000, 640);
        _app.MoveTo(120, 90);
        var before = _app.Window.BoundingRectangle;

        _app.Restart();

        var after = _app.Window.BoundingRectangle;
        Assert.True(
            Math.Abs(after.Left - before.Left) <= 2 && Math.Abs(after.Top - before.Top) <= 2
            && Math.Abs(after.Width - before.Width) <= 2 && Math.Abs(after.Height - before.Height) <= 2,
            $"The window was at {before} before the restart and at {after} after it.");
    }

    [Fact]
    public void A_second_launch_brings_the_running_window_forward()
    {
        var window = _app.Window.Patterns.Window.Pattern;
        window.SetWindowVisualState(WindowVisualState.Minimized);
        AppSession.WaitUntil(() => window.WindowVisualState.Value == WindowVisualState.Minimized,
            () => "The window did not minimize.");

        _app.LaunchSecondInstance();

        AppSession.WaitUntil(() => window.WindowVisualState.Value == WindowVisualState.Normal,
            () => $"The window is still {window.WindowVisualState.Value} after the second launch.");
        var handle = _app.Window.Properties.NativeWindowHandle.Value;
        AppSession.WaitUntil(() => AppSession.ForegroundWindow() == handle,
            () => "The running window did not come to the foreground after the second launch.");
    }
}
