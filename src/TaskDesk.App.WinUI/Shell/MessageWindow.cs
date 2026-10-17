using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TaskDesk.App.Shell;

/// <summary>
/// Handles one window message. Return true when handled; <paramref name="result"/> is then the message's result.
/// </summary>
internal delegate bool WindowMessageHook(uint message, nint wParam, nint lParam, out nint result);

/// <summary>
/// A hidden message-only window (parent HWND_MESSAGE) for the shell's Win32 messages, such as WM_HOTKEY; it
/// works while every app window is hidden. Hooks see each message in the order they were added; the first that
/// handles it wins. Create, use and dispose it on the UI thread, whose message loop serves it.
/// </summary>
/// <remarks>
/// A message-only window gets posted and sent messages, but no broadcasts and no WM_QUERYENDSESSION /
/// WM_ENDSESSION (Windows sends those to top-level windows only).
/// </remarks>
internal sealed unsafe partial class MessageWindow : IDisposable
{
    private const string ClassName = "TaskDeskMessageWindow";
    private const nint HwndMessage = -3;

    // Window handle -> instance, for the static window procedure.
    private static readonly Dictionary<nint, MessageWindow> Windows = [];
    private static bool _classRegistered;

    private readonly List<WindowMessageHook> _hooks = [];

    public MessageWindow()
    {
        var instance = GetModuleHandleW(null);
        RegisterClassOnce(instance);
        Handle = CreateWindowExW(0, ClassName, ClassName, 0, 0, 0, 0, 0, HwndMessage, 0, instance, 0);
        if (Handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The message window could not be created.");
        }

        Windows[Handle] = this;
    }

    public nint Handle { get; private set; }

    public void AddHook(WindowMessageHook hook) => _hooks.Add(hook);

    public void RemoveHook(WindowMessageHook hook) => _hooks.Remove(hook);

    public void Dispose()
    {
        if (Handle == 0)
        {
            return;
        }

        Windows.Remove(Handle);
        DestroyWindow(Handle);
        Handle = 0;
        _hooks.Clear();
    }

    private static void RegisterClassOnce(nint instance)
    {
        if (_classRegistered)
        {
            return;
        }

        fixed (char* name = ClassName)
        {
            var windowClass = new WndClassEx
            {
                Size = (uint)sizeof(WndClassEx),
                WndProc = &WndProc,
                Instance = instance,
                ClassName = name,
            };
            if (RegisterClassExW(&windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The message window class could not be registered.");
            }
        }

        _classRegistered = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (Windows.TryGetValue(hwnd, out var window))
        {
            foreach (var hook in window._hooks.ToArray())
            {
                try
                {
                    if (hook(message, wParam, lParam, out var result))
                    {
                        return result;
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // An exception must not unwind into native code; the UI thread reports it instead.
                    var failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(failure.Throw);
                    return 0;
                }
            }
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(WndClassEx* windowClass);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);
}
