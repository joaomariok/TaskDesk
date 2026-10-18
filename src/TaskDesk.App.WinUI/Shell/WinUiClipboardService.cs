using System.Runtime.InteropServices;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Shell;

/// <summary>
/// Puts text on the Windows clipboard for Settings' "Copy path" buttons. Win32, like the WPF app's clipboard: the
/// WinRT clipboard refuses an app whose window lacks the focus, and the text stays after TaskDesk exits.
/// </summary>
internal sealed class WinUiClipboardService(IWin32Clipboard clipboard, Action<TimeSpan> wait) : IClipboardService
{
    private const int OpenAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(20);

    public WinUiClipboardService()
        : this(new Win32Clipboard(), Thread.Sleep)
    {
    }

    public void SetText(string text)
    {
        // Another app may hold the clipboard open for a moment. Copying is a convenience, so a missed copy is not an error.
        if (!TryOpen())
        {
            return;
        }

        try
        {
            clipboard.Empty();
            clipboard.SetUnicodeText(text);
        }
        finally
        {
            clipboard.Close();
        }
    }

    private bool TryOpen()
    {
        for (var attempt = 1; attempt <= OpenAttempts; attempt++)
        {
            if (clipboard.Open())
            {
                return true;
            }

            if (attempt < OpenAttempts)
            {
                wait(RetryDelay);
            }
        }

        return false;
    }
}

/// <summary>The Win32 clipboard calls <see cref="WinUiClipboardService"/> makes, so its tests need not touch the real clipboard.</summary>
internal interface IWin32Clipboard
{
    /// <summary>Opens the clipboard for this thread; false while another app holds it open.</summary>
    bool Open();

    /// <summary>Clears the open clipboard and takes ownership of it.</summary>
    void Empty();

    /// <summary>Puts <paramref name="text"/> on the open clipboard as Unicode text; false when it could not.</summary>
    bool SetUnicodeText(string text);

    void Close();
}

/// <summary>The real Win32 clipboard.</summary>
internal sealed partial class Win32Clipboard : IWin32Clipboard
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    public bool Open() => OpenClipboard(0);

    public void Empty() => EmptyClipboard();

    public bool SetUnicodeText(string text)
    {
        var bytes = (text.Length + 1) * sizeof(char);
        var memory = GlobalAlloc(GmemMoveable, (nuint)bytes);
        if (memory == 0)
        {
            return false;
        }

        var target = GlobalLock(memory);
        if (target == 0)
        {
            GlobalFree(memory);
            return false;
        }

        Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
        Marshal.WriteInt16(target, text.Length * sizeof(char), 0);
        GlobalUnlock(memory);
        // The clipboard owns the memory once SetClipboardData succeeds.
        if (SetClipboardData(CfUnicodeText, memory) == 0)
        {
            GlobalFree(memory);
            return false;
        }

        return true;
    }

    public void Close() => CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint format, nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalFree(nint memory);
}
