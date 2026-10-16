namespace TaskDesk.Core.Shell;

/// <summary>The per-user Windows Run key (HKCU\Software\Microsoft\Windows\CurrentVersion\Run).</summary>
public interface IRunKey
{
    string? GetValue(string name);
    void SetValue(string name, string value);
    void Remove(string name);
}
