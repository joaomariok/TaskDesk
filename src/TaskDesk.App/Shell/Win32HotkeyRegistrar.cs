using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Shell;

/// <summary>
/// Global hotkey through Win32 RegisterHotKey on a hidden message-only window, so it works while the
/// main window is hidden. Create it on the UI thread. Covered by manual checks only.
/// </summary>
internal sealed class Win32HotkeyRegistrar : IHotkeyRegistrar
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const int HwndMessage = -3;

    private readonly ILogger<Win32HotkeyRegistrar> _logger;
    private readonly HwndSource _source;
    private Hotkey? _current;
    private int _currentId;

    public Win32HotkeyRegistrar(ILogger<Win32HotkeyRegistrar> logger)
    {
        _logger = logger;
        _source = new HwndSource(new HwndSourceParameters("TaskDeskHotkeyWindow")
        {
            ParentWindow = new IntPtr(HwndMessage),
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        });
        _source.AddHook(WndProc);
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
        if (!RegisterHotKey(_source.Handle, newId, ToWin32Modifiers(hotkey.Modifiers) | ModNoRepeat, (uint)hotkey.VirtualKey))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            _logger.LogWarning("Global hotkey {Hotkey} could not be registered: {Error}", hotkey, error);
            return false;
        }

        if (_current is not null)
        {
            UnregisterHotKey(_source.Handle, _currentId);
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

        UnregisterHotKey(_source.Handle, _currentId);
        _current = null;
        _currentId = 0;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private static uint ToWin32Modifiers(HotkeyModifiers modifiers) => (uint)modifiers;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _current is not null && wParam.ToInt32() == _currentId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
