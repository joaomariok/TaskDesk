using TaskDesk.App.Shell;

namespace TaskDesk.Views.Tests;

/// <summary>
/// Settings' "Copy path" buttons put the path on the Windows clipboard (the shell source is linked in). These tests
/// run the service against a fake of the Win32 clipboard: the real clipboard is the person's, shared with every
/// app, so the gate must not write it. The real round trip is the UI suite's
/// <c>SettingsWindowTests.Copy_path_puts_the_data_folder_on_the_clipboard</c>.
/// </summary>
[TestClass]
public sealed class ClipboardServiceTests
{
    [TestMethod]
    public void SetText_replaces_the_clipboard_contents_with_the_text_and_closes_it()
    {
        var clipboard = new FakeWin32Clipboard();

        Service(clipboard).SetText(@"C:\Users\me\AppData\Local\TaskDesk");

        CollectionAssert.AreEqual(
            new[] { "Open", "Empty", @"SetUnicodeText C:\Users\me\AppData\Local\TaskDesk", "Close" }, clipboard.Calls);
    }

    [TestMethod]
    public void A_clipboard_another_app_holds_open_for_a_moment_is_tried_again()
    {
        var clipboard = new FakeWin32Clipboard { RefusedOpens = 2 };
        var waits = new List<TimeSpan>();

        Service(clipboard, waits.Add).SetText("path");

        CollectionAssert.AreEqual(new[] { "Open", "Open", "Open", "Empty", "SetUnicodeText path", "Close" }, clipboard.Calls);
        CollectionAssert.AreEqual(new[] { TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20) }, waits);
    }

    [TestMethod]
    public void A_clipboard_that_stays_held_is_given_up_after_five_tries_without_an_error()
    {
        var clipboard = new FakeWin32Clipboard { RefusedOpens = int.MaxValue };

        Service(clipboard).SetText("path");

        CollectionAssert.AreEqual(Enumerable.Repeat("Open", 5).ToList(), clipboard.Calls,
            "A missed copy is not an error, and a clipboard it could not open is neither emptied nor closed.");
    }

    [TestMethod]
    public void A_clipboard_that_refuses_the_text_is_still_closed()
    {
        var clipboard = new FakeWin32Clipboard { RefusesText = true };

        Service(clipboard).SetText("path");

        CollectionAssert.AreEqual(new[] { "Open", "Empty", "SetUnicodeText path", "Close" }, clipboard.Calls);
    }

    private static WinUiClipboardService Service(FakeWin32Clipboard clipboard, Action<TimeSpan>? wait = null) =>
        new(clipboard, wait ?? (_ => { }));

    private sealed class FakeWin32Clipboard : IWin32Clipboard
    {
        public int RefusedOpens { get; init; }

        public bool RefusesText { get; init; }

        public List<string> Calls { get; } = [];

        public bool Open()
        {
            Calls.Add("Open");
            return Calls.Count(c => c == "Open") > RefusedOpens;
        }

        public void Empty() => Calls.Add("Empty");

        public bool SetUnicodeText(string text)
        {
            Calls.Add($"SetUnicodeText {text}");
            return !RefusesText;
        }

        public void Close() => Calls.Add("Close");
    }
}
