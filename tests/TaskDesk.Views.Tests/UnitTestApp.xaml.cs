using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The test host: a WinUI app that runs the tests on Microsoft.Testing.Platform once it has a UI thread,
/// then exits with the run's exit code. The generated WinUI entry point returns void, so the code must be
/// set on <see cref="Environment.ExitCode"/>, or a failing run would exit 0 and pass the gate.
/// </summary>
public partial class UnitTestApp : Application
{
    // The process's own stderr, opened before the test platform redirects Console to capture each test's output.
    private static readonly TextWriter CrashLog = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };

    private Window? _window;

    public UnitTestApp()
    {
        InitializeComponent();
        ReportCrashes();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "TaskDesk.Views.Tests" };
        _window.Activate();
        UITestMethodAttribute.DispatcherQueue = _window.DispatcherQueue;
        try
        {
            Environment.ExitCode = await MicrosoftTestingPlatformApplication.RunAsync(
                Environment.GetCommandLineArgs()[1..]);
        }
        finally
        {
            _window.Close();
            Exit();
        }
    }

    // An exception no test catches (an async void handler that outlives its window, say) ends the host with a bare
    // 0xC000027B and no test name. Write it out first so the gate log names the cause. The exception stays
    // unhandled: the host still dies and the run still fails. An unobserved task exception does not end the host,
    // so it is written as a warning.
    private void ReportCrashes()
    {
        UnhandledException += (_, e) => Report(Crash, "unhandled XAML exception", e.Exception, e.Message);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report(Crash, "unhandled exception", e.ExceptionObject as Exception, null);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Report("TEST HOST WARNING", "unobserved task exception", e.Exception, null);
    }

    private const string Crash = "TEST HOST CRASH";

    private static void Report(string heading, string kind, Exception? exception, string? message)
    {
        lock (CrashLog)
        {
            CrashLog.WriteLine($"{heading} ({kind}): {exception?.GetType().FullName}: {exception?.Message}");
            // WinUI's message can carry more than the exception's own (the originating error).
            if (message is { Length: > 0 } && message != exception?.Message)
            {
                CrashLog.WriteLine($"  {message}");
            }

            CrashLog.WriteLine(exception);
        }
    }
}
