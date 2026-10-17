using System.Diagnostics;
using System.Drawing;
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
    /// <summary>Set to a TaskDesk.App.exe (for example a published build) to test it instead of the Debug build.</summary>
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

    /// <param name="theme">The <c>themeMode</c> setting: System, Light or Dark.</param>
    /// <param name="globalHotkey">The <c>globalHotkey</c> setting; empty registers none.</param>
    public static AppSession Start(string theme = "System", string globalHotkey = "")
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "TaskDesk-uitest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);
        // Closing the window exits (so layout and pending edits are flushed), no first-close hint, and by default no global hotkey.
        File.WriteAllText(Path.Combine(dataDirectory, "settings.json"), $$"""
            { "closeBehavior": "Exit", "trayHintShown": true, "startWithWindows": false, "globalHotkey": "{{globalHotkey}}",
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
        var process = Process.GetProcessById(_app!.ProcessId);
        Window.Close();
        Assert.True(process.WaitForExit(Timeout), "TaskDesk did not exit after its window was closed.");
        _app.Dispose();
        _app = null;
        Launch();
    }

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

        return list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Select(item => item.Name).ToList();
    }

    public void WaitForRows(string listAutomationId, params string[] expected) =>
        WaitUntil(() => Rows(listAutomationId).SequenceEqual(expected),
            () => $"{listAutomationId}: expected [{string.Join(", ", expected)}], " +
                  $"was [{string.Join(", ", Rows(listAutomationId))}].");

    /// <summary>Selects a Task row and gives it keyboard focus, so list shortcuts (Space, Delete, F2) apply to it.</summary>
    public void SelectRow(string listAutomationId, string title)
    {
        var item = Eventually(
            () => Find(listAutomationId).FindFirstChild(cf => cf.ByControlType(ControlType.ListItem).And(cf.ByName(title))),
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

    /// <summary>Resizes the main window to a width and height in device-independent pixels (what WPF measures).</summary>
    public void ResizeTo(int width, int height)
    {
        var scale = GetDpiForWindow(Window.Properties.NativeWindowHandle.Value) / 96.0;
        Window.Patterns.Transform.Pattern.Resize(width * scale, height * scale);
        WaitUntil(() => Math.Abs(Window.BoundingRectangle.Width - width * scale) < 4,
            () => $"The window did not resize to {width} (is {Window.BoundingRectangle.Width / scale}).");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

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
        // Let the window finish painting after it comes to the front.
        Thread.Sleep(300);
        FlaUI.Core.Capturing.Capture.Element(Window).ToFile(path);
    }

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

    /// <summary>Drags an element (a splitter) horizontally by <paramref name="dx"/> physical pixels.</summary>
    public static void DragBy(AutomationElement element, int dx)
    {
        var bounds = element.BoundingRectangle;
        var start = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        Mouse.Drag(start, new Point(start.X + dx, start.Y));
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

    private void Launch()
    {
        var start = new ProcessStartInfo(AppPath()) { UseShellExecute = false };
        start.Environment["TASKDESK_DATA_DIR"] = _dataDirectory;
        start.Environment.Remove("TASKDESK_SANDBOX");
        _app = Application.Launch(start);
        Window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(20))
                 ?? throw new InvalidOperationException("TaskDesk showed no main window.");
        Window.SetForeground();
        // The window shows before its first load finishes; quick add is enabled once it has.
        WaitUntil(() => Find(Ids.QuickAddBox).IsEnabled, () => "Quick add never became ready.");
    }

    private static string AppPath()
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
