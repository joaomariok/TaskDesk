using System.Diagnostics;
using System.IO;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Shell;

internal sealed class ExplorerFolderOpener : IFolderOpener
{
    public void Open(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
