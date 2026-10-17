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
    private Window? _window;

    public UnitTestApp() => InitializeComponent();

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
}
