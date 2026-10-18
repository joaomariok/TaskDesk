using System.Runtime.InteropServices;

namespace TaskDesk.UiTests;

/// <summary>
/// Reads and writes Unicode text on the real Windows clipboard through Win32, which (unlike the WinRT clipboard)
/// does not need the foreground. Only the opt-in UI suite touches the real clipboard; the gate never does.
/// </summary>
internal static class SystemClipboard
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    /// <summary>The clipboard's text, or null when it holds none or another app has it open.</summary>
    public static string? ReadText()
    {
        if (!OpenClipboard(0))
        {
            return null;
        }

        try
        {
            var handle = GetClipboardData(CfUnicodeText);
            if (handle == 0)
            {
                return null;
            }

            var pointer = GlobalLock(handle);
            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Puts <paramref name="text"/> back on the clipboard (to restore the person's own text).</summary>
    public static void WriteText(string text)
    {
        if (!OpenClipboard(0))
        {
            return;
        }

        try
        {
            EmptyClipboard();
            var memory = GlobalAlloc(GmemMoveable, (nuint)((text.Length + 1) * sizeof(char)));
            if (memory == 0)
            {
                return;
            }

            var target = GlobalLock(memory);
            Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
            Marshal.WriteInt16(target, text.Length * sizeof(char), 0);
            GlobalUnlock(memory);
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint memory);
}
