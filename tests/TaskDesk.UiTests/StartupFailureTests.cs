using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace TaskDesk.UiTests;

/// <summary>
/// The start-up failure path in a real process (#56): when the database cannot be opened, a dialog explains it and
/// the app exits with code 1 once the owner presses OK. Framework-neutral: the WPF app shows a message box, the
/// WinUI app a content dialog on a window of its own; both have the explanation and an OK button.
/// </summary>
public sealed class StartupFailureTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "TaskDesk-uitest-" + Guid.NewGuid().ToString("N"));

    private readonly UIA3Automation _automation = new();
    private Process? _process;

    [Fact]
    public void A_database_that_cannot_be_opened_is_explained_in_a_dialog_and_the_app_exits_with_code_1()
    {
        Directory.CreateDirectory(_dataDirectory);
        File.WriteAllText(Path.Combine(_dataDirectory, "taskdesk.db"), "this is not a sqlite database, just text");
        var start = new ProcessStartInfo(AppSession.AppPath()) { UseShellExecute = false };
        start.Environment["TASKDESK_DATA_DIR"] = _dataDirectory;
        start.Environment.Remove("TASKDESK_SANDBOX");
        _process = Process.Start(start) ?? throw new InvalidOperationException("TaskDesk did not start.");
        using var app = FlaUI.Core.Application.Attach(_process.Id);

        Window? dialogWindow = null;
        var deadline = Stopwatch.StartNew();
        while (dialogWindow is null && deadline.Elapsed < Timeout)
        {
            dialogWindow = app.GetAllTopLevelWindows(_automation).FirstOrDefault(ExplainsTheFailure);
            if (dialogWindow is null)
            {
                Thread.Sleep(200);
            }
        }

        Assert.True(dialogWindow is not null, "No dialog explained that the database could not be opened.");
        var ok = AppSession.Eventually(
            () => dialogWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("OK"))),
            "The failure dialog has no OK button.");
        ok.AsButton().Invoke();

        Assert.True(_process.WaitForExit(Timeout), "TaskDesk did not exit after OK.");
        Assert.Equal(1, _process.ExitCode);
    }

    private static bool ExplainsTheFailure(Window window)
    {
        try
        {
            return window.FindAllDescendants().Any(e =>
                (e.Properties.Name.ValueOrDefault ?? "").Contains("could not open its database", StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The tree changes under us while the app starts; look again.
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(Timeout);
            }
        }
        finally
        {
            _process?.Dispose();
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
}
