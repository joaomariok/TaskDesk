namespace TaskDesk.Core.Shell;

/// <summary>Keeps the Run key entry in line with the "Start with Windows" setting.</summary>
public sealed class AutoStartService(IRunKey runKey)
{
    public const string ValueName = "TaskDesk";
    public const string MinimizedFlag = "--minimized";

    /// <summary>Writes the entry for <paramref name="exePath"/> when enabled, removes it otherwise.</summary>
    public void Apply(bool enabled, string exePath)
    {
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
