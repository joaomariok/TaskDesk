using TaskDesk.Core.Paths;
using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public sealed class AutoStartServiceTests
{
    private sealed class FakeRunKey : IRunKey
    {
        public Dictionary<string, string> Values { get; } = [];
        public string? GetValue(string name) => Values.GetValueOrDefault(name);
        public void SetValue(string name, string value) => Values[name] = value;
        public void Remove(string name) => Values.Remove(name);
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
        key.Values["TaskDesk"] = "x";
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
    public void Sandbox_enabled_never_writes_the_entry()
    {
        var key = new FakeRunKey();
        new AutoStartService(key, new RuntimeMode(true)).Apply(true, @"C:\x.exe");

        Assert.Empty(key.Values);
    }

    [Fact]
    public void Sandbox_disabled_never_removes_the_entry()
    {
        var key = new FakeRunKey();
        key.Values["TaskDesk"] = "x";
        new AutoStartService(key, new RuntimeMode(true)).Apply(false, @"C:\x.exe");

        Assert.Equal("x", key.Values["TaskDesk"]);
    }

    [Fact]
    public void Normal_mode_still_writes_the_entry()
    {
        var key = new FakeRunKey();
        new AutoStartService(key, RuntimeMode.Normal).Apply(true, @"C:\x.exe");

        Assert.Equal("\"C:\\x.exe\" --minimized", key.Values["TaskDesk"]);
    }
}
