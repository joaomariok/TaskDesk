using System.Runtime.InteropServices;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Shell;

/// <summary>
/// Puts text on the Windows clipboard for Settings' "Copy path" buttons. Win32, like the WPF app's clipboard: the
/// WinRT clipboard refuses an app whose window lacks the focus, and the text stays after TaskDesk exits.
/// </summary>
internal sealed partial class WinUiClipboardService : IClipboardService
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const int OpenAttempts = 5;

    public void SetText(string text)
    {
        // Another app may hold the clipboard open for a moment. Copying is a convenience, so a missed copy is not an error.
        if (!TryOpen())
        {
            return;
        }

        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * sizeof(char);
            var memory = GlobalAlloc(GmemMoveable, (nuint)bytes);
            if (memory == 0)
            {
                return;
            }

            var target = GlobalLock(memory);
            if (target == 0)
            {
                GlobalFree(memory);
                return;
            }

            Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
            Marshal.WriteInt16(target, text.Length * sizeof(char), 0);
            GlobalUnlock(memory);
            // The clipboard owns the memory once SetClipboardData succeeds.
            if (SetClipboardData(CfUnicodeText, memory) == 0)
            {
                GlobalFree(memory);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool TryOpen()
    {
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(0))
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

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
