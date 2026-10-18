using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.App.Shell;

namespace TaskDesk.Views.Tests;

/// <summary>
/// Settings' "Copy path" buttons put the path on the Windows clipboard. The shell source is linked in. The test
/// reads the clipboard through Win32, which (unlike the WinRT clipboard) does not need the foreground.
/// </summary>
[TestClass]
public sealed partial class ClipboardServiceTests
{
    private const uint CfUnicodeText = 13;

    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task SetText_puts_the_text_on_the_clipboard()
    {
        var ct = TestContext.CancellationToken;
        // Put back the owner's text afterwards, when there is some.
        var previous = ReadText();
        var text = $@"C:\TaskDesk-clipboard-test\{Guid.NewGuid():N}";
        try
        {
            new WinUiClipboardService().SetText(text);

            await UiWait.UntilAsync(() => ReadText() == text, () => "The clipboard does not hold the copied text.", ct);
        }
        finally
        {
            if (previous is not null)
            {
                new WinUiClipboardService().SetText(previous);
            }
        }
    }

    private static string? ReadText()
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

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);
}
