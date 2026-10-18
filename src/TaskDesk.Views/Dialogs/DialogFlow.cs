using Microsoft.UI.Xaml;

namespace TaskDesk.Views.Dialogs;

/// <summary>
/// The dialogs and file pickers of one operation (from <see cref="DialogService.BeginFlow"/>). Its first step opens on
/// the active window, and every later step on that same window, even if the owner activated another one meanwhile
/// or another window's dialog closed in between. Other dialogs are not affected: a dialog started from another
/// window opens on that window.
/// </summary>
public sealed class DialogFlow
{
    private readonly DialogService _service;
    private Window? _owner;

    internal DialogFlow(DialogService service) => _service = service;

    /// <summary>The window this flow's steps open on, resolved when the first step opens.</summary>
    public Window Owner => _owner ??= _service.OwnerWindow();

    /// <summary>Shows the dialog on <see cref="Owner"/>; see <see cref="DialogService.ShowAsync(DialogRequest)"/>.</summary>
    public Task<DialogResult> ShowAsync(DialogRequest request) => _service.ShowAsync(request, Owner);

    /// <summary>A confirmation on <see cref="Owner"/>; see <see cref="DialogService.ConfirmAsync(string, string, string, bool)"/>.</summary>
    public Task<bool> ConfirmAsync(string title, string body, string primaryText, bool destructive = false) =>
        _service.ConfirmAsync(title, body, primaryText, destructive, Owner);

    /// <summary>Shows a dialog or a file picker on <see cref="Owner"/>.</summary>
    public Task<T> ShowOnOwnerAsync<T>(Func<Window, Task<T>> show)
    {
        ArgumentNullException.ThrowIfNull(show);
        return show(Owner);
    }
}
