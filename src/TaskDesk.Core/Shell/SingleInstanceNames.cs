using System.Security.Cryptography;
using System.Text;

namespace TaskDesk.Core.Shell;

/// <summary>
/// Names of the single-instance mutex and activation pipe. The real data folder keeps the shipped names; a redirected
/// one (<c>TASKDESK_DATA_DIR</c>, a trial or test run) gets names of its own, so it never wakes or blocks the
/// owner's running TaskDesk, and two redirected folders can run side by side.
/// </summary>
public sealed record SingleInstanceNames(string Mutex, string Pipe)
{
    private const string MutexBase = @"Local\TaskDesk.SingleInstance";
    private const string PipeBase = "TaskDesk.Activate";

    public static SingleInstanceNames For(string? dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return new SingleInstanceNames(MutexBase, PipeBase);
        }

        // Windows paths are case-insensitive; normalise so one folder always maps to one instance.
        var normalised = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory.Trim()))
            .ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));
        var suffix = Convert.ToHexString(hash, 0, 8);
        return new SingleInstanceNames($"{MutexBase}.{suffix}", $"{PipeBase}.{suffix}");
    }
}
