using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TaskDesk.Views.Tests;

/// <summary>
/// A shown window for dialog tests, and the means to find the content dialog open on it and press its buttons
/// the way a click does (through the button's automation peer).
/// </summary>
public sealed class DialogHost : IDisposable
{
    private DialogHost(Window window)
    {
        Window = window;
    }

    public Window Window { get; }

    public static async Task<DialogHost> OpenAsync(CancellationToken cancellationToken)
    {
        var window = new Window { Title = "Dialog host", Content = new Grid() };
        window.Activate();
        var host = new DialogHost(window);
        await UiWait.UntilAsync(() => window.Content.XamlRoot is not null, () => "The host window never loaded.", cancellationToken);
        return host;
    }

    /// <summary>The content dialog open on <paramref name="root"/>, once it is shown.</summary>
    public static async Task<ContentDialog> OpenDialogAsync(XamlRoot root, CancellationToken cancellationToken)
    {
        ContentDialog? dialog = null;
        await UiWait.UntilAsync(() => (dialog = OpenDialog(root)) is not null, () => "No content dialog opened.", cancellationToken);
        // Wait for the template and the content, so the buttons and texts exist.
        await UiWait.UntilAsync(
            () => Buttons(dialog!).Any() && dialog!.Content is FrameworkElement { IsLoaded: true },
            () => "The dialog's buttons and content never appeared.", cancellationToken);
        return dialog!;
    }

    public static ContentDialog? OpenDialog(XamlRoot root) =>
        VisualTreeHelper.GetOpenPopupsForXamlRoot(root)
            .Select(popup => popup.Child)
            .SelectMany(child => child is null ? [] : VisualTree.Descendants(child).Prepend(child))
            .OfType<ContentDialog>()
            .FirstOrDefault();

    /// <summary>The dialog's visible buttons, left to right, by their text.</summary>
    public static IEnumerable<Button> Buttons(ContentDialog dialog) =>
        VisualTree.Descendants(dialog).OfType<Button>()
            .Where(b => b.Name is "PrimaryButton" or "SecondaryButton" or "CloseButton" && b.Visibility == Visibility.Visible);

    /// <summary>Presses one of the dialog's buttons: PrimaryButton, SecondaryButton or CloseButton.</summary>
    public static void Press(ContentDialog dialog, string buttonName)
    {
        var button = VisualTree.Find<Button>(dialog, buttonName);
        new ButtonAutomationPeer(button).Invoke();
    }

    /// <summary>The texts of every text block in the dialog's content and title.</summary>
    public static IReadOnlyList<string> Texts(ContentDialog dialog) =>
        VisualTree.Descendants(dialog).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Length > 0).ToList();

    public void Dispose() => Window.Close();
}
