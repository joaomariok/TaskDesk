using Microsoft.Extensions.Logging;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public sealed class AutoStartServiceTests
{
    private const string Exe = @"C:\Apps\Task Desk\TaskDesk.exe";
    private const string OwnCommand = "\"C:\\Apps\\Task Desk\\TaskDesk.exe\" --minimized";

    private static readonly RuntimeMode RedirectedDataFolder = RuntimeMode.Resolve(@"C:\temp\taskdesk", null);
    private static readonly RuntimeMode RedirectedDataFolderSandboxOff = RuntimeMode.Resolve(@"C:\temp\taskdesk", "0");

    private sealed class FakeRunKey : IRunKey
    {
        public Dictionary<string, string> Values { get; } = [];
        public string? GetValue(string name) => Values.GetValueOrDefault(name);
        public void SetValue(string name, string value) => Values[name] = value;
        public void Remove(string name) => Values.Remove(name);
    }

    private sealed class CapturingLogger : ILogger<AutoStartService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public void Enabled_writes_quoted_exe_path_with_minimized_flag()
    {
        var key = new FakeRunKey();
        new AutoStartService(key).Apply(true, @"C:\Apps\Task Desk\TaskDesk.exe");

        Assert.Equal("\"C:\\Apps\\Task Desk\\TaskDesk.exe\" --minimized", key.Values["TaskDesk"]);
    }

    [Fact]
    public void Enabled_overwrites_a_stale_path()
    {
        var key = new FakeRunKey();
        key.Values["TaskDesk"] = "\"C:\\Old\\TaskDesk.exe\" --minimized";
        new AutoStartService(key).Apply(true, @"C:\New\TaskDesk.exe");

        Assert.Equal("\"C:\\New\\TaskDesk.exe\" --minimized", key.Values["TaskDesk"]);
    }

    [Fact]
    public void Disabled_removes_the_entry()
    {
        var key = new FakeRunKey();
        key.Values["TaskDesk"] = "\"C:\\New\\TaskDesk.exe\" --minimized";
        new AutoStartService(key).Apply(false, @"C:\New\TaskDesk.exe");

        Assert.Empty(key.Values);
    }

    [Fact]
    public void Disabled_without_an_entry_is_a_no_op()
    {
        var key = new FakeRunKey();
        new AutoStartService(key).Apply(false, @"C:\x.exe");

        Assert.Empty(key.Values);
    }

    [Fact]
    public void Disabled_leaves_an_entry_that_points_at_another_exe_and_logs_it()
    {
        var key = new FakeRunKey();
        key.Values["TaskDesk"] = "\"C:\\Real\\TaskDesk.exe\" --minimized";
        var logger = new CapturingLogger();
        new AutoStartService(key, null, logger).Apply(false, @"C:\Dev\TaskDesk.exe");

        Assert.Equal("\"C:\\Real\\TaskDesk.exe\" --minimized", key.Values["TaskDesk"]);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information);
    }

    [Theory]
    [InlineData("\"C:\\Apps\\Task Desk\\TaskDesk.exe\" --minimized")]
    [InlineData("\"C:\\Apps\\Task Desk\\TaskDesk.exe\"")]
    [InlineData("\"c:\\apps\\task desk\\TASKDESK.EXE\" --minimized")]
    [InlineData("\"C:\\Apps\\Other\\..\\Task Desk\\TaskDesk.exe\" --minimized")]
    public void Disabled_removes_our_own_entry_in_any_stored_form(string stored)
    {
        var key = new FakeRunKey();
        key.Values["TaskDesk"] = stored;
        new AutoStartService(key).Apply(false, Exe);

        Assert.Empty(key.Values);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"")]
    [InlineData("\"C:\\Apps\\Other.exe\" --minimized")]
    [InlineData("\"C:\\Apps\\Task Desk\\TaskDesk.exe.bak\" --minimized")]
    public void Disabled_leaves_an_entry_it_cannot_match(string stored)
    {
        var key = new FakeRunKey();
        key.Values["TaskDesk"] = stored;
        new AutoStartService(key).Apply(false, Exe);

        Assert.Equal(stored, key.Values["TaskDesk"]);
    }

    [Fact]
    public void Sandbox_enabled_never_writes_the_entry()
    {
        var key = new FakeRunKey();
        new AutoStartService(key, RedirectedDataFolder).Apply(true, @"C:\x.exe");

        Assert.Empty(key.Values);
    }

    [Fact]
    public void Sandbox_disabled_never_removes_the_entry()
    {
        var key = new FakeRunKey();
        key.Values["TaskDesk"] = "x";
        new AutoStartService(key, RedirectedDataFolder).Apply(false, @"C:\x.exe");

        Assert.Equal("x", key.Values["TaskDesk"]);
    }

    [Fact]
    public void Redirected_data_folder_with_sandbox_off_never_writes_the_entry()
    {
        var key = new FakeRunKey();
        new AutoStartService(key, RedirectedDataFolderSandboxOff).Apply(true, @"C:\x.exe");

        Assert.Empty(key.Values);
    }

    [Fact]
    public void Redirected_data_folder_with_sandbox_off_never_removes_our_own_entry()
    {
        var key = new FakeRunKey();
        key.Values["TaskDesk"] = OwnCommand;
        new AutoStartService(key, RedirectedDataFolderSandboxOff).Apply(false, Exe);

        Assert.Equal(OwnCommand, key.Values["TaskDesk"]);
    }

    [Fact]
    public void Normal_mode_still_writes_the_entry()
    {
        var key = new FakeRunKey();
        new AutoStartService(key, RuntimeMode.Normal).Apply(true, @"C:\x.exe");

        Assert.Equal("\"C:\\x.exe\" --minimized", key.Values["TaskDesk"]);
    }

    [Fact]
    public void Normal_mode_still_removes_our_own_entry()
    {
        var key = new FakeRunKey();
        key.Values["TaskDesk"] = OwnCommand;
        new AutoStartService(key, RuntimeMode.Normal).Apply(false, Exe);

        Assert.Empty(key.Values);
    }
}
