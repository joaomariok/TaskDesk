using TaskDesk.Core.Paths;

namespace TaskDesk.Core.Shell;

/// <summary>
/// Keeps the Run key entry in line with the "Start with Windows" setting. In sandbox mode it never writes
/// or removes the entry.
/// </summary>
public sealed class AutoStartService(IRunKey runKey, RuntimeMode? mode = null)
{
    public const string ValueName = "TaskDesk";
    public const string MinimizedFlag = "--minimized";

    /// <summary>Writes the entry for <paramref name="exePath"/> when enabled, removes it otherwise.</summary>
    public void Apply(bool enabled, string exePath)
    {
        if (mode?.IsSandbox == true)
        {
            return;
        }

        if (enabled)
        {
            runKey.SetValue(ValueName, $"\"{exePath}\" {MinimizedFlag}");
        }
        else
        {
            runKey.Remove(ValueName);
        }
    }
}
