using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaskDesk.Core.Paths;

namespace TaskDesk.Core.Shell;

/// <summary>
/// Keeps the Run key entry in line with the "Start with Windows" setting. It never touches the entry when the
/// data folder is redirected, and only removes an entry that points at this exe.
/// </summary>
public sealed class AutoStartService(
    IRunKey runKey, RuntimeMode? mode = null, ILogger<AutoStartService>? logger = null)
{
    public const string ValueName = "TaskDesk";
    public const string MinimizedFlag = "--minimized";

    private readonly ILogger _logger = logger ?? NullLogger<AutoStartService>.Instance;

    /// <summary>Writes the entry for <paramref name="exePath"/> when enabled, removes our own entry otherwise.</summary>
    public void Apply(bool enabled, string exePath)
    {
        if (mode?.ManagesAutoStart == false)
        {
            return;
        }

        if (enabled)
        {
            runKey.SetValue(ValueName, $"\"{exePath}\" {MinimizedFlag}");
            return;
        }

        var stored = runKey.GetValue(ValueName);
        if (stored is null)
        {
            return;
        }

        if (PointsAt(stored, exePath))
        {
            runKey.Remove(ValueName);
        }
        else
        {
            _logger.LogInformation(
                "Left the auto-start entry in place: it points at {Command}, not at {ExePath}", stored, exePath);
        }
    }

    private static bool PointsAt(string command, string exePath)
    {
        var stored = ParseExePath(command);
        if (stored is null)
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(stored), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>The quoted path at the start of the command; an unquoted command is taken up to its flag.</summary>
    private static string? ParseExePath(string command)
    {
        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        var flag = text.IndexOf($" {MinimizedFlag}", StringComparison.OrdinalIgnoreCase);
        var path = (flag >= 0 ? text[..flag] : text).Trim();
        return path.Length == 0 ? null : path;
    }
}
