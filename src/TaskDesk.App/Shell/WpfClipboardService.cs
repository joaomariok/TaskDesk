using System.Runtime.InteropServices;
using System.Windows;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Shell;

/// <summary>Copies text to the Windows clipboard.</summary>
internal sealed class WpfClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        try
        {
            // Flush so the text outlives TaskDesk.
            Clipboard.SetDataObject(text, copy: true);
        }
        catch (COMException)
        {
            // Another app holds the clipboard open: copying is a convenience, so a missed copy is not an error.
        }
    }
}
