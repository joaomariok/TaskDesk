using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Xunit.Sdk;

namespace TaskDesk.UiTests;

/// <summary>
/// One TaskDesk process against a fresh temp data folder, driven through Windows UI Automation. The redirected
/// data folder puts the app in sandbox mode (no Run key, no notification registration) and gives it its own
/// single-instance names, so it never wakes the owner's running TaskDesk. Dispose kills the process and deletes
/// the folder.
/// </summary>
public sealed class AppSession : IDisposable
{
    /// <summary>
    /// Set to a TaskDesk.App.exe (for example a published build) to test it instead of the Debug build;
    /// scripts/ui-tests.ps1 -Publish sets it.
    /// </summary>
    public const string AppVariable = "TASKDESK_UI_APP";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly UIA3Automation _automation = new();
    private readonly string _dataDirectory;
    private Application? _app;

    private AppSession(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    public Window Window { get; private set; } = null!;

    /// <summary>The temp data folder this TaskDesk runs against (TASKDESK_DATA_DIR).</summary>
    public string DataDirectory => _dataDirectory;

    /// <param name="theme">The <c>themeMode</c> setting: System, Light or Dark.</param>
    /// <param name="globalHotkey">The <c>globalHotkey</c> setting; empty registers none.</param>
    /// <param name="closeBehavior">The <c>closeBehavior</c> setting: Exit (the default here) or HideToTray.</param>
    public static AppSession Start(string theme = "System", string globalHotkey = "", string closeBehavior = "Exit")
    {
        // On a locked desktop the lock screen covers the window and every launch would time out; fail at once.
        if (IsDesktopLocked())
        {
            throw new InvalidOperationException(
                $"{DesktopLockedPrefix} the input desktop is not the user's; unlock it and rerun the UI tests.");
        }

        var dataDirectory = Path.Combine(Path.GetTempPath(), "TaskDesk-uitest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);
        // By default closing the window exits (so layout and pending edits are flushed); no first-close hint, and by
        // default no global hotkey.
        File.WriteAllText(Path.Combine(dataDirectory, "settings.json"), $$"""
            { "closeBehavior": "{{closeBehavior}}", "trayHintShown": true, "startWithWindows": false, "globalHotkey": "{{globalHotkey}}",
              "themeMode": "{{theme}}" }
            """);
        var session = new AppSession(dataDirectory);
        try
        {
            session.Launch();
        }
        catch
        {
            session.Dispose();
            throw;
        }

        return session;
    }

    /// <summary>Closes the window (which exits the app), waits for the process to end, and starts it again.</summary>
    public void Restart()
    {
        CloseAndWaitForExit();
        Launch();
    }

    /// <summary>
    /// Does to the app what Windows does at sign-out: WM_QUERYENDSESSION, then WM_ENDSESSION, to each of its
    /// top-level windows (hidden ones too), waiting for each answer; then ends the process if it still runs, as
    /// Windows does. Starts it again on the same data folder.
    /// </summary>
    public void EndSessionAndRestart()
    {
        var process = Process.GetProcessById(_app!.ProcessId);
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (GetWindowThreadProcessId(hwnd, out var owner) != 0 && owner == process.Id)
            {
                windows.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);
        Assert.NotEmpty(windows);
        foreach (var hwnd in windows)
        {
            SendEndSessionMessage(hwnd, WmQueryEndSession, IntPtr.Zero);
        }

        foreach (var hwnd in windows)
        {
            SendEndSessionMessage(hwnd, WmEndSession, 1);
        }

        // Once every window has answered WM_ENDSESSION, Windows ends the process: whatever it had not saved is lost.
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It had exited by itself.
        }

        Assert.True(process.WaitForExit(Timeout), "TaskDesk did not end with the session.");
        _app.Dispose();
        _app = null;
        Launch();
    }

    // Sends one session-end message and waits for the answer, as Windows does. A window closed meanwhile (the app
    // exits on the first message) answers nothing, which is fine; one that does not answer in time would get
    // Windows' "This app is preventing shutdown" screen.
    private static void SendEndSessionMessage(IntPtr hwnd, uint message, IntPtr wParam)
    {
        if (SendMessageTimeoutW(hwnd, message, wParam, EndSessionLogoff, SmtoAbortIfHung, 5_000, out _) == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Assert.True(error != ErrorTimeout, $"TaskDesk did not answer session-end message 0x{message:X4} in time.");
        }
    }

    private const int ErrorTimeout = 1460;
    private const uint WmQueryEndSession = 0x0011;
    private const uint WmEndSession = 0x0016;
    private const uint SmtoAbortIfHung = 0x0002;
    private static readonly IntPtr EndSessionLogoff = unchecked((IntPtr)0x80000000L);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutW(
        IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    /// <summary>Closes the window (with the Exit close behaviour) and asserts the process ends.</summary>
    public void CloseAndWaitForExit()
    {
        var process = Process.GetProcessById(_app!.ProcessId);
        Window.Close();
        Assert.True(process.WaitForExit(Timeout), "TaskDesk did not exit after its window was closed.");
        _app.Dispose();
        _app = null;
    }

    /// <summary>
    /// Starts the app a second time on the same data folder and asserts that this second process hands over to
    /// the running one and exits.
    /// </summary>
    public void LaunchSecondInstance()
    {
        var start = new ProcessStartInfo(AppPath()) { UseShellExecute = false };
        start.Environment["TASKDESK_DATA_DIR"] = _dataDirectory;
        start.Environment.Remove("TASKDESK_SANDBOX");
        using var second = Process.Start(start) ?? throw new InvalidOperationException("The second launch did not start.");
        if (!second.WaitForExit(Timeout))
        {
            second.Kill(entireProcessTree: true);
            Assert.Fail("The second launch did not exit; it should hand over to the running TaskDesk.");
        }
    }

    /// <summary>Moves the main window's top-left corner to a screen point in physical pixels.</summary>
    public void MoveTo(int left, int top)
    {
        Window.Patterns.Transform.Pattern.Move(left, top);
        WaitUntil(() => Math.Abs(Window.BoundingRectangle.Left - left) < 4 && Math.Abs(Window.BoundingRectangle.Top - top) < 4,
            () => $"The window did not move to ({left}, {top}) (is {Window.BoundingRectangle}).");
    }

    /// <summary>True while the app's process runs.</summary>
    public bool IsRunning => _app is { HasExited: false };

    /// <summary>True while a window is shown (not hidden; a minimized window counts as shown).</summary>
    public static bool IsWindowShown(IntPtr handle) => IsWindowVisible(handle);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    /// <summary>The window handle that has the foreground (the active window of the desktop).</summary>
    public static IntPtr ForegroundWindow() => GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public AutomationElement Find(string automationId) =>
        Eventually(() => Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            $"No element with AutomationId '{automationId}'.");

    public TextBox TextBox(string automationId) => Find(automationId).AsTextBox();

    /// <summary>The Task titles in a Task list, or none when the list is hidden.</summary>
    public IReadOnlyList<string> Rows(string listAutomationId)
    {
        var list = Window.FindFirstDescendant(cf => cf.ByAutomationId(listAutomationId));
        if (list is null || list.IsOffscreen)
        {
            return [];
        }

        return RowItems(list).Select(item => item.Name).ToList();
    }

    /// <summary>A row of a list by its title, or null when the list does not show it.</summary>
    public AutomationElement? FindRow(string listAutomationId, string title) =>
        RowItems(Find(listAutomationId)).FirstOrDefault(item => item.Name == title);

    // A list's rows in order: its ListItem children, and those of its groups. The Task list groups its rows (Overdue,
    // open, Completed), and UI Automation shows each group as a Group child holding its rows.
    private static IEnumerable<AutomationElement> RowItems(AutomationElement list) =>
        list.FindAllChildren().SelectMany(child => child.ControlType switch
        {
            ControlType.ListItem => [child],
            ControlType.Group => child.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)),
            _ => [],
        });

    public void WaitForRows(string listAutomationId, params string[] expected) =>
        WaitUntil(() => Rows(listAutomationId).SequenceEqual(expected),
            () => $"{listAutomationId}: expected [{string.Join(", ", expected)}], " +
                  $"was [{string.Join(", ", Rows(listAutomationId))}].");

    /// <summary>Selects a Task row and gives it keyboard focus, so list shortcuts (Space, Delete, F2) apply to it.</summary>
    public void SelectRow(string listAutomationId, string title)
    {
        var item = Eventually(
            () => FindRow(listAutomationId, title),
            $"No row '{title}' in {listAutomationId}; rows were [{string.Join(", ", Rows(listAutomationId))}], " +
            $"quick add held '{TextBox(Ids.QuickAddBox).Text}'.").AsListBoxItem();
        item.Select();
        item.Focus();
        WaitUntil(() => item.IsSelected && item.Properties.HasKeyboardFocus.ValueOrDefault, () => $"Row '{title}' did not take focus.");
    }

    /// <summary>
    /// Resizes the window to <paramref name="factor"/> times its current width (the default is 900 px wide;
    /// the icon rail starts below that). A ratio keeps the test independent of the display scale.
    /// </summary>
    public void ResizeWidth(double factor)
    {
        var bounds = Window.BoundingRectangle;
        var target = bounds.Width * factor;
        Window.Patterns.Transform.Pattern.Resize(target, bounds.Height);
        WaitUntil(() => Math.Abs(Window.BoundingRectangle.Width - target) < 4,
            () => $"The window did not resize to {factor:P0} of its width.");
    }

    /// <summary>Looks in every window of the desktop: pop-ups (flyouts, the sidebar overlay) are windows of their own.</summary>
    public AutomationElement? FindOnDesktop(string automationId) =>
        _automation.GetDesktop().FindFirstDescendant(cf => cf.ByAutomationId(automationId));

    /// <summary>The name of the element with keyboard focus (a Task row is named by its title).</summary>
    public string? FocusedName() => _automation.FocusedElement()?.Properties.Name.ValueOrDefault;

    /// <summary>Resizes the main window to a width and height in device-independent pixels.</summary>
    public void ResizeTo(int width, int height)
    {
        var scale = GetDpiForWindow(Window.Properties.NativeWindowHandle.Value) / 96.0;
        Window.Patterns.Transform.Pattern.Resize(width * scale, height * scale);
        WaitUntil(() => Math.Abs(Window.BoundingRectangle.Width - width * scale) < 4,
            () => $"The window did not resize to {width} (is {Window.BoundingRectangle.Width / scale}).");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    // The focused element's type, class and process, for a failure message: the focus can be outside the app.
    private string DescribeFocused()
    {
        try
        {
            var focused = _automation.FocusedElement();
            if (focused is null)
            {
                return "nothing";
            }

            var process = focused.Properties.ProcessId.ValueOrDefault;
            var owner = process == _app?.ProcessId ? "TaskDesk" : $"process {process}";
            return $"{focused.Properties.ControlType.ValueOrDefault} '{focused.Properties.Name.ValueOrDefault}', " +
                   $"class {focused.Properties.ClassName.ValueOrDefault}, in {owner}";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ex.Message;
        }
    }

    public string? FocusedAutomationId() => _automation.FocusedElement()?.Properties.AutomationId.ValueOrDefault;

    /// <summary>Gives an element keyboard focus and waits until it has it.</summary>
    public void Focus(string automationId)
    {
        Find(automationId).Focus();
        WaitUntil(() => FocusedAutomationId() == automationId, () => $"{automationId} did not take focus.");
    }

    /// <summary>Focuses a text box and types into it, then presses Enter if asked.</summary>
    public void TypeInto(string automationId, string text, bool pressEnter = false)
    {
        Focus(automationId);
        Keyboard.Type(text);
        // The app can still be working through the keys (the WinUI text box takes a while per key); Enter
        // or a focus change sent before it has would reach another element, or the rest of the text would.
        WaitUntil(() => TextBox(automationId).Text.Contains(text, StringComparison.Ordinal),
            () => $"{automationId} did not receive '{text}'; it holds '{TextBox(automationId).Text}'.");
        if (pressEnter)
        {
            Keyboard.Type(VirtualKeyShort.RETURN);
        }
    }

    public static void Press(params VirtualKeyShort[] keys) => Keyboard.TypeSimultaneously(keys);

    public static void Ctrl(VirtualKeyShort key) => Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, key);

    /// <summary>Saves a PNG of the main window as it is on screen; the window is brought to the front first.</summary>
    public void Capture(string path)
    {
        Window.SetForeground();
        CaptureFrame(Window, path);
    }

    /// <summary>
    /// Saves a PNG of what a window draws on screen, without its invisible resize border. The mouse is moved
    /// off the window first, so no hover highlight from an earlier click lands in the shot.
    /// </summary>
    private static void CaptureFrame(Window window, string path)
    {
        var frame = VisibleFrame(window);
        Mouse.Position = frame.Left >= 40
            ? new Point(frame.Left - 20, frame.Top + frame.Height / 2)
            : new Point(frame.Right + 20, frame.Top + frame.Height / 2);
        // Let the window drop the hover and finish painting.
        Thread.Sleep(300);
        FlaUI.Core.Capturing.Capture.Rectangle(frame).ToFile(path);
    }

    // The window's bounding rectangle includes the invisible resize border, which shows whatever is behind the
    // window and differs from run to run; DWM's frame bounds are only what the window draws.
    private static Rectangle VisibleFrame(Window window)
    {
        var result = DwmGetWindowAttribute(window.Properties.NativeWindowHandle.Value, DwmwaExtendedFrameBounds,
            out var frame, System.Runtime.InteropServices.Marshal.SizeOf<NativeRect>());
        return result == 0
            ? Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom)
            : window.BoundingRectangle;
    }

    private const int DwmwaExtendedFrameBounds = 9;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect value, int size);

    /// <summary>The quick-add window, once it is shown.</summary>
    public Window QuickAddWindow() =>
        Eventually(() => _app!.GetAllTopLevelWindows(_automation).FirstOrDefault(w => w.Title == "TaskDesk quick add"),
            "The quick-add window did not show.");

    /// <summary>The Settings window, once it is shown.</summary>
    public Window SettingsWindow() =>
        Eventually(() => _app!.GetAllTopLevelWindows(_automation).FirstOrDefault(w => w.Title == "TaskDesk Settings"),
            "The Settings window did not show.");

    /// <summary>True while the Settings window is shown.</summary>
    public bool SettingsWindowShown() =>
        _app!.GetAllTopLevelWindows(_automation).Any(w => w.Title == "TaskDesk Settings");

    /// <summary>True while the quick-add window is shown.</summary>
    public bool QuickAddWindowShown() =>
        _app!.GetAllTopLevelWindows(_automation).Any(w => w.Title == "TaskDesk quick add");

    /// <summary>
    /// The bounds of the element once it has stopped moving. A ListView's rows slide in with an entrance transition
    /// after a reload, and UI Automation reports the bounds mid-slide (about 10 px low), so a point aimed at a row
    /// read too early lands on the wrong part of it.
    /// </summary>
    public static Rectangle SettledBounds(Func<AutomationElement> find)
    {
        var last = Rectangle.Empty;
        var settled = Rectangle.Empty;
        WaitUntil(
            () =>
            {
                var bounds = find().BoundingRectangle;
                var same = !bounds.IsEmpty && bounds == last;
                last = bounds;
                if (!same)
                {
                    Thread.Sleep(150);
                    return false;
                }

                settled = bounds;
                return true;
            },
            () => $"The element did not stop moving (last at {last}).");
        return settled;
    }

    /// <summary>A drag a person makes: press, move in small steps (drag and drop needs real pointer moves; the cursor
    /// jumps of Mouse.Drag and Mouse.Position send none to WinUI), pause over the target, release.
    /// <paramref name="beforeRelease"/> runs over the target, with the button still down.</summary>
    public static void DragSlowly(Point from, Point to, Action? beforeRelease = null)
    {
        MoveTo(from);
        Thread.Sleep(100);
        Mouse.Down(MouseButton.Left);
        Thread.Sleep(150);
        const int Steps = 40;
        for (var i = 1; i <= Steps; i++)
        {
            MoveTo(new Point(from.X + (to.X - from.X) * i / Steps, from.Y + (to.Y - from.Y) * i / Steps));
            Thread.Sleep(15);
        }

        Thread.Sleep(400);
        if (beforeRelease is not null)
        {
            beforeRelease();
            Thread.Sleep(200);
        }

        Mouse.Up(MouseButton.Left);
        Thread.Sleep(200);
    }

    private static void MoveTo(Point point)
    {
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);
        var input = new Input
        {
            Type = InputMouse,
            Mouse = new MouseInput
            {
                X = (int)Math.Round((point.X - left) * 65535.0 / (width - 1)),
                Y = (int)Math.Round((point.Y - top) * 65535.0 / (height - 1)),
                Flags = MouseEventFMove | MouseEventFAbsolute | MouseEventFVirtualDesk,
            },
        };
        Assert.Equal(1u, SendInput(1, [input], Marshal.SizeOf<Input>()));
    }

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint InputMouse = 0;
    private const uint MouseEventFMove = 0x0001;
    private const uint MouseEventFAbsolute = 0x8000;
    private const uint MouseEventFVirtualDesk = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    // INPUT: the type, then a union whose largest member (MOUSEINPUT on x64) is all a mouse move needs.
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);

    /// <summary>
    /// Drags an element (a splitter) horizontally by <paramref name="dx"/> physical pixels, with the pointer moves a
    /// person's drag makes (<see cref="DragSlowly"/>), so the splitter resizes through its own manipulation.
    /// </summary>
    public static void DragBy(AutomationElement element, int dx)
    {
        var bounds = element.BoundingRectangle;
        var start = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        DragSlowly(start, new Point(start.X + dx, start.Y));
    }

    public static T Eventually<T>(Func<T?> probe, string failure) where T : class
    {
        T? value = null;
        WaitUntil(() => (value = probe()) is not null, () => failure);
        return value!;
    }

    public static void WaitUntil(Func<bool> condition, Func<string> failure)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            bool met;
            try
            {
                met = condition();
            }
            catch (Exception ex) when (ex is not XunitException)
            {
                // The tree changes under us while the app updates; try again.
                met = false;
            }

            if (met)
            {
                return;
            }

            if (deadline.Elapsed > Timeout)
            {
                Assert.Fail(failure());
            }

            Thread.Sleep(100);
        }
    }

    /// <summary>Folder (under the repository's TestResults) where a failed test leaves a PNG of the window.</summary>
    public static string FailuresDirectory => Path.Combine(RepoRoot(), "TestResults", "ui-failures");

    public void Dispose()
    {
        try
        {
            if (_app is not null)
            {
                CaptureIfFailed();
            }
        }
        finally
        {
            // Whatever happened above, the process and any child it started must not outlive the test.
            KillProcessTree();
            _app?.Dispose();
            _automation.Dispose();
            try
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
            catch (IOException)
            {
                // A just-killed process can hold the database for a moment; the folder is in %TEMP%.
            }
        }
    }

    private void KillProcessTree()
    {
        if (_app is null)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(_app.ProcessId);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(Timeout);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }

    /// <summary>
    /// After a failed test, saves the window as it was, so a flaky failure can be seen and not just read about.
    /// Needs the session to be disposed after the test body (a test class field), when xUnit has the result.
    /// </summary>
    private void CaptureIfFailed()
    {
        if (TestContext.Current.TestState?.Result != TestResult.Failed || _app!.HasExited)
        {
            return;
        }

        try
        {
            var name = TestContext.Current.Test?.TestDisplayName ?? "unknown";
            name = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            Directory.CreateDirectory(FailuresDirectory);
            var path = Path.Combine(FailuresDirectory, name + ".png");
            Capture(path);
            Console.WriteLine($"UI failure screenshot: {path}");
        }
        catch (Exception ex)
        {
            // A missing screenshot must never hide the real failure.
            Console.WriteLine($"UI failure screenshot not saved: {ex.Message}");
        }
    }

    // Puts a window above every other non-topmost window, without activating it: in and out of the topmost band.
    private static void RaiseWithoutActivating(IntPtr hwnd)
    {
        const uint flags = SwpNoMove | SwpNoSize | SwpNoActivate;
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, flags);
        SetWindowPos(hwnd, HwndNoTopmost, 0, 0, 0, 0, flags);
    }

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint GaRoot = 2;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    /// <summary>Starts a failure message caused by a locked desktop; scripts/ui-tests.ps1 stops the run on it.</summary>
    public const string DesktopLockedPrefix = "DESKTOP LOCKED:";

    /// <summary>
    /// True while the session does not take input: the input desktop is "Default" while it does, and a locked
    /// session switches it to Winlogon, which a normal process cannot open.
    /// </summary>
    public static bool IsDesktopLocked()
    {
        var desktop = OpenInputDesktop(0, false, DesktopReadObjects);
        if (desktop == IntPtr.Zero)
        {
            return true;
        }

        try
        {
            var name = new char[256];
            if (!GetUserObjectInformationW(desktop, UoiName, name, name.Length * sizeof(char), out _))
            {
                return true;
            }

            return new string(name).TrimEnd('\0') != "Default";
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }

    private const uint DesktopReadObjects = 0x0001;
    private const int UoiName = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(IntPtr obj, int index, [Out] char[] info, int length, out int needed);

    private void Launch()
    {
        var start = new ProcessStartInfo(AppPath()) { UseShellExecute = false };
        start.Environment["TASKDESK_DATA_DIR"] = _dataDirectory;
        start.Environment.Remove("TASKDESK_SANDBOX");
        _app = Application.Launch(start);
        Window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(20))
                 ?? throw new InvalidOperationException("TaskDesk showed no main window.");
        // Windows can refuse SetForeground while another app is in front, and the test's keys would then go to that
        // app. A click on the title bar is input, which always activates the window; the app still picks the element
        // that takes focus.
        Window.SetForeground();
        // Refused, the new window opens behind the app in front (after a restart, the one Windows activated when the
        // old window closed), and the click would land on that app instead. Raising the window in the z-order is not
        // activation, so Windows allows it; the click then reaches the title bar.
        var handle = Window.Properties.NativeWindowHandle.Value;
        RaiseWithoutActivating(handle);
        var bounds = Window.BoundingRectangle;
        var titleBar = new Point(bounds.Left + bounds.Width / 2, bounds.Top + 16);
        WaitUntil(() => GetAncestor(WindowFromPoint(titleBar), GaRoot) == handle,
            () => IsDesktopLocked()
                ? $"{DesktopLockedPrefix} the lock screen covers TaskDesk's title bar at {titleBar}."
                : $"Another window covers TaskDesk's title bar at {titleBar}.");
        Mouse.Click(titleBar);
        // The window shows before its first load finishes; quick add is enabled once it has.
        WaitUntil(() => Find(Ids.QuickAddBox).IsEnabled, () => "Quick add never became ready.");
        // Wait for start-up focus to land in quick add, without moving it there, so a test's first shortcut
        // reaches the app and start-up focus stays tested.
        WaitUntil(() => FocusedAutomationId() == Ids.QuickAddBox,
            () => $"Start-up focus never reached quick add; it is on '{FocusedAutomationId()}' ({DescribeFocused()}).");
    }

    internal static string AppPath()
    {
        var overridePath = Environment.GetEnvironmentVariable(AppVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        // The Debug build of the app, wherever its target framework and runtime put it; the newest wins.
        var bin = Path.Combine(RepoRoot(), @"src\TaskDesk.App\bin\Debug");
        var newest = Directory.Exists(bin)
            ? new DirectoryInfo(bin).EnumerateFiles("TaskDesk.App.exe", SearchOption.AllDirectories)
                .MaxBy(file => file.LastWriteTimeUtc)
            : null;
        return newest?.FullName
               ?? throw new FileNotFoundException($"Build the app first (dotnet build src/TaskDesk.App) or set {AppVariable}.", bin);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaskDesk.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
