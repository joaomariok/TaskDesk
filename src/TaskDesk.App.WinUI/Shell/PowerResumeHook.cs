namespace TaskDesk.App.Shell;

/// <summary>
/// Calls back when the PC resumes from sleep, so Reminders due meanwhile are checked at once (SPEC 7.4). It
/// reads WM_POWERBROADCAST on a top-level <see cref="MessageWindow"/>, the message WPF's
/// <c>SystemEvents.PowerModeChanged</c> is raised from (without the WPF/WinForms-only package). Both resume
/// notifications count: automatic (always sent) and suspend (after user input); the scheduler coalesces them.
/// </summary>
internal sealed class PowerResumeHook : IDisposable
{
    private const uint WmPowerBroadcast = 0x0218;
    private const nint PbtApmResumeSuspend = 0x0007;
    private const nint PbtApmResumeAutomatic = 0x0012;

    private readonly MessageWindow _window;
    private readonly Action _onResume;

    public PowerResumeHook(MessageWindow window, Action onResume)
    {
        _window = window;
        _onResume = onResume;
        _window.AddHook(OnMessage);
    }

    public void Dispose() => _window.RemoveHook(OnMessage);

    private bool OnMessage(uint message, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (message == WmPowerBroadcast && wParam is PbtApmResumeAutomatic or PbtApmResumeSuspend)
        {
            _onResume();
        }

        return false; // others may want it too; DefWindowProc answers TRUE
    }
}
