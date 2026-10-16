using System.IO;
using System.Windows;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace NotificationSpike;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "NotificationSpike.log");
    private static readonly List<string> HistoryLines = [];

    public static event Action<string>? LogWritten;

    public static IReadOnlyList<string> History => HistoryLines;

    public static string StatusLine { get; private set; } = "";

    public static void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [pid {Environment.ProcessId}] {message}";
        lock (HistoryLines)
        {
            HistoryLines.Add(line);
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch (IOException)
            {
            }
        }

        LogWritten?.Invoke(line);
    }

    public static void ShowToast()
    {
        try
        {
            var toast = new AppNotificationBuilder()
                .AddArgument("task", "spike-123")
                .AddText("Spike reminder")
                .AddText("Body click opens the task; buttons carry actions.")
                .AddButton(new AppNotificationButton("Mark done").AddArgument("action", "done"))
                .AddButton(new AppNotificationButton("Snooze 10 min").AddArgument("action", "snooze10"))
                .AddButton(new AppNotificationButton("Snooze 1 h").AddArgument("action", "snooze60"))
                .BuildNotification();
            AppNotificationManager.Default.Show(toast);
            Log($"Show() returned; notification Id={toast.Id}. Windows setting: {AppNotificationManager.Default.Setting}");
        }
        catch (Exception ex)
        {
            Log($"Show() FAILED: {ex}");
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log($"Startup. Command line: {Environment.CommandLine}");
        try
        {
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnNotificationInvoked;
            manager.Register();
            Log("Register() succeeded.");
            StatusLine = "Register() succeeded. Log file: " + LogPath;

            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Log($"Activation kind at startup: {activation.Kind}");
            if (activation.Kind == ExtendedActivationKind.AppNotification
                && activation.Data is AppNotificationActivatedEventArgs args)
            {
                Log("Launched BY a notification. " + Describe(args));
            }
        }
        catch (Exception ex)
        {
            StatusLine = "Register() FAILED: " + ex.GetType().Name + ": " + ex.Message;
            Log($"Register()/activation FAILED: {ex}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception ex)
        {
            Log($"Unregister failed: {ex.Message}");
        }

        base.OnExit(e);
    }

    private static void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) =>
        Log("NotificationInvoked in RUNNING process. " + Describe(args));

    private static string Describe(AppNotificationActivatedEventArgs args) =>
        "Arguments: " + string.Join(", ", args.Arguments.Select(p => $"{p.Key}={p.Value}"))
        + (args.UserInput.Count > 0
            ? "; UserInput: " + string.Join(", ", args.UserInput.Select(p => $"{p.Key}={p.Value}"))
            : "");
}
