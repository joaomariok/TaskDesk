using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Shell;

/// <summary>
/// Global hotkey through Win32 RegisterHotKey on the shell's message-only window, so it works while the main
/// window is hidden. Pressed is raised on the UI thread, whose message loop serves that window. Create and
/// use it on the UI thread. It does not own the window.
/// </summary>
internal sealed partial class WinUiHotkeyRegistrar : IHotkeyRegistrar
{
    private const uint WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private readonly MessageWindow _window;
    private readonly ILogger<WinUiHotkeyRegistrar> _logger;
    private Hotkey? _current;
    private int _currentId;

    public WinUiHotkeyRegistrar(MessageWindow window, ILogger<WinUiHotkeyRegistrar> logger)
    {
        _window = window;
        _logger = logger;
        _window.AddHook(OnMessage);
    }

    public event Action? Pressed;

    public bool TryRegister(Hotkey hotkey)
    {
        if (hotkey == _current)
        {
            return true;
        }

        // Register under the other id first, so a failure leaves the working hotkey untouched.
        var newId = _currentId == 1 ? 2 : 1;
        if (!RegisterHotKey(_window.Handle, newId, (uint)hotkey.Modifiers | ModNoRepeat, (uint)hotkey.VirtualKey))
        {
            var error = new Win32Exception(Marshal.GetLastPInvokeError()).Message;
            _logger.LogWarning("Global hotkey {Hotkey} could not be registered: {Error}", hotkey, error);
            return false;
        }

        if (_current is not null)
        {
            UnregisterHotKey(_window.Handle, _currentId);
        }

        _current = hotkey;
        _currentId = newId;
        _logger.LogInformation("Global hotkey {Hotkey} registered", hotkey);
        return true;
    }

    public void Unregister()
    {
        if (_current is null)
        {
            return;
        }

        UnregisterHotKey(_window.Handle, _currentId);
        _current = null;
        _currentId = 0;
    }

    public void Dispose()
    {
        Unregister();
        _window.RemoveHook(OnMessage);
    }

    private bool OnMessage(uint message, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (message != WmHotkey || _current is null || wParam != _currentId)
        {
            return false;
        }

        Pressed?.Invoke();
        return true;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);
}
