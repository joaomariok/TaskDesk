using System.Runtime.InteropServices;

namespace TaskDesk.App.Shell;

/// <summary>
/// Replaces WPF's <c>SessionEnding</c>: when Windows signs out or shuts down, runs the given work (the shell's
/// exit, which flushes the pending detail edit and stops the host) and answers only once it has finished. The
/// UI thread keeps pumping messages meanwhile, so the work's continuations on the dispatcher still run. The
/// session is never blocked. Needs a top-level <see cref="MessageWindow"/>: a message-only one gets no
/// WM_QUERYENDSESSION. Use on the UI thread.
/// </summary>
internal sealed unsafe partial class SessionEndHook : IDisposable
{
    private const uint WmNull = 0x0000;
    private const uint WmQueryEndSession = 0x0011;
    private const uint WmEndSession = 0x0016;

    private readonly MessageWindow _window;
    private readonly Func<Task> _onSessionEnding;
    private Task? _ending;

    public SessionEndHook(MessageWindow window, Func<Task> onSessionEnding)
    {
        _window = window;
        _onSessionEnding = onSessionEnding;
        _window.AddHook(OnMessage);
    }

    public void Dispose() => _window.RemoveHook(OnMessage);

    private bool OnMessage(uint message, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        switch (message)
        {
            case WmQueryEndSession:
                WaitPumping(_ending ??= _onSessionEnding());
                result = 1; // TRUE: the session may end
                return true;
            case WmEndSession when wParam != 0:
                // The session ends for sure; the process can be ended once this returns.
                WaitPumping(_ending ??= _onSessionEnding());
                return true;
            default:
                return false;
        }
    }

    // Runs a nested message loop until the task completes; a continuation posts WM_NULL to wake GetMessage.
    private void WaitPumping(Task task)
    {
        if (task.IsCompleted)
        {
            return;
        }

        var handle = _window.Handle;
        _ = task.ContinueWith(_ => PostMessageW(handle, WmNull, 0, 0), TaskScheduler.Default);
        Msg msg;
        while (!task.IsCompleted)
        {
            var got = GetMessageW(&msg, 0, 0, 0);
            if (got == 0)
            {
                PostQuitMessage((int)msg.WParam); // leave WM_QUIT for the outer loop
                return;
            }

            if (got < 0)
            {
                return;
            }

            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(Msg* msg, nint hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(Msg* msg);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(Msg* msg);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);
}
