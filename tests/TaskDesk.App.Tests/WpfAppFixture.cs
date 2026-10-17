using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using TaskDesk.App.Styles;
using TaskDesk.Core.Paths;

namespace TaskDesk.App.Tests;

/// <summary>
/// The one TaskDesk <see cref="App"/> of the test process, on its own STA thread. Only one
/// <see cref="System.Windows.Application"/> can exist per AppDomain, so every test that needs it shares this
/// fixture through <see cref="WpfAppCollection"/>.
/// </summary>
/// <remarks>
/// It loads App.xaml (ThemeMode first, then the merged style dictionaries) and installs the foundation
/// brushes, as start-up does. <c>OnStartup</c> (host, database in %LOCALAPPDATA%, tray, hotkey) must not run.
/// The Application constructor queues it on the thread's dispatcher, so the thread never pumps that
/// dispatcher: it runs test work from its own queue instead. Layout, templates and resource lookups are
/// synchronous and need no pumping.
/// </remarks>
public sealed class WpfAppFixture : IDisposable
{
    private readonly BlockingCollection<Action> _work = [];
    private readonly Thread _thread;

    public WpfAppFixture()
    {
        // Should start-up ever run, it finds a throwaway data folder, not the owner's.
        Environment.SetEnvironmentVariable(
            AppPaths.DataDirectoryVariable,
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"TaskDesk-app-tests-{Environment.ProcessId}"));

        _thread = new Thread(() =>
        {
            foreach (var work in _work.GetConsumingEnumerable())
            {
                work();
            }
        })
        {
            IsBackground = true,
            Name = "WPF app fixture",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        Run(() =>
        {
            var app = new App();
            // App.OnStartup raises Startup before anything else; stop it there if the dispatcher ever runs it.
            app.Startup += (_, _) => throw new InvalidOperationException("App.OnStartup must not run in tests.");
            app.InitializeComponent();
            FoundationResources.Install(app);
            App = app;
        });
    }

    public App App { get; private set; } = null!;

    /// <summary>Runs <paramref name="action"/> on the app's thread; its exception is rethrown here.</summary>
    public void Run(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        using var done = new ManualResetEventSlim();
        _work.Add(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait();
        failure?.Throw();
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        _thread.Join();
        _work.Dispose();
    }
}

[CollectionDefinition(Name)]
public sealed class WpfAppCollection : ICollectionFixture<WpfAppFixture>
{
    public const string Name = "WPF app";
}
