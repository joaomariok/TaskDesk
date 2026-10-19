using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public sealed class UninstallCleanupTests : IDisposable
{
    private const string Exe = @"C:\Users\x\AppData\Local\TaskDesk.App\current\TaskDesk.App.exe";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "taskdesk-uninstall-" + Guid.NewGuid().ToString("N"));

    private sealed class FakeRunKey : IRunKey
    {
        public Dictionary<string, string> Values { get; } = [];
        public string? GetValue(string name) => Values.GetValueOrDefault(name);
        public void SetValue(string name, string value) => Values[name] = value;
        public void Remove(string name) => Values.Remove(name);
    }

    private sealed class BrokenRunKey : IRunKey
    {
        public string? GetValue(string name) => throw new UnauthorizedAccessException("Run key denied.");
        public void SetValue(string name, string value) => throw new UnauthorizedAccessException("Run key denied.");
        public void Remove(string name) => throw new UnauthorizedAccessException("Run key denied.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Removes_the_Run_entry_that_points_at_this_exe()
    {
        var key = new FakeRunKey();
        key.Values["TaskDesk"] = $"\"{Exe}\" --minimized";

        UninstallCleanup.Run(key, Exe, () => { });

        Assert.Empty(key.Values);
    }

    [Fact]
    public void Leaves_a_Run_entry_that_points_at_another_exe()
    {
        var key = new FakeRunKey();
        key.Values["TaskDesk"] = "\"D:\\Portable\\TaskDesk\\TaskDesk.App.exe\" --minimized";

        UninstallCleanup.Run(key, Exe, () => { });

        Assert.Equal("\"D:\\Portable\\TaskDesk\\TaskDesk.App.exe\" --minimized", key.Values["TaskDesk"]);
    }

    [Fact]
    public void Removes_the_notification_registration()
    {
        var unregistered = 0;

        UninstallCleanup.Run(new FakeRunKey(), Exe, () => unregistered++);

        Assert.Equal(1, unregistered);
    }

    [Fact]
    public void Leaves_the_data_folder_and_its_files_untouched()
    {
        var data = Directory.CreateDirectory(Path.Combine(_root, "TaskDesk")).FullName;
        var database = Path.Combine(data, "taskdesk.db");
        var settings = Path.Combine(data, "settings.json");
        File.WriteAllText(database, "tasks");
        File.WriteAllText(settings, "{}");
        var exe = Path.Combine(_root, "TaskDesk.App", "current", "TaskDesk.App.exe");
        var key = new FakeRunKey();
        key.Values["TaskDesk"] = $"\"{exe}\" --minimized";

        UninstallCleanup.Run(key, exe, () => { });

        Assert.Equal("tasks", File.ReadAllText(database));
        Assert.Equal("{}", File.ReadAllText(settings));
        Assert.Equal(2, Directory.GetFiles(data).Length);
    }

    [Fact]
    public void A_failing_Run_key_still_removes_the_notification_registration_and_reports_the_failure()
    {
        var unregistered = 0;

        var error = Assert.Throws<AggregateException>(
            () => UninstallCleanup.Run(new BrokenRunKey(), Exe, () => unregistered++));

        Assert.Equal(1, unregistered);
        Assert.IsType<UnauthorizedAccessException>(Assert.Single(error.InnerExceptions));
    }

    [Fact]
    public void A_failing_notification_cleanup_still_removes_the_Run_entry_and_reports_the_failure()
    {
        var key = new FakeRunKey();
        key.Values["TaskDesk"] = $"\"{Exe}\" --minimized";

        var error = Assert.Throws<AggregateException>(
            () => UninstallCleanup.Run(key, Exe, () => throw new InvalidOperationException("No activator.")));

        Assert.Empty(key.Values);
        Assert.IsType<InvalidOperationException>(Assert.Single(error.InnerExceptions));
    }
}
