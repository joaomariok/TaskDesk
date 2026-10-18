using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace TaskDesk.Views.Dialogs;

/// <summary>
/// Shows every confirmation and message as a native content dialog on the active window: the WinUI counterpart
/// of the WPF app's shared Fluent dialog, with its rules. The primary choice is the Enter default unless the
/// request is destructive, when Cancel is; Cancel and Esc are the same.
/// </summary>
/// <param name="activeWindow">The window the dialog opens on; it must be shown.</param>
public sealed class DialogService(Func<Window?> activeWindow)
{
    /// <summary>The <see cref="DialogResult.ChoiceIndex"/> of Cancel and Esc.</summary>
    public const int CancelIndex = -1;

    /// <summary>The AutomationId the UI tests find a dialog by, kept from the WPF Fluent dialog.</summary>
    public const string AutomationId = "FluentDialog";

    /// <summary>
    /// Starts an operation of several steps (such as Import's file, then its Merge/Replace choice, then the Replace
    /// confirmation): every step shown through the returned flow opens on the window its first step opened on.
    /// </summary>
    public DialogFlow BeginFlow() => new(this);

    /// <summary>Shows the dialog on <see cref="OwnerWindow"/> and returns what the owner chose.</summary>
    public Task<DialogResult> ShowAsync(DialogRequest request) => ShowAsync(request, OwnerWindow());

    internal async Task<DialogResult> ShowAsync(DialogRequest request, Window owner)
    {
        // A content dialog has three buttons: the primary choice, at most one other, and Cancel.
        if (request.Choices.Count is < 1 or > 2)
        {
            throw new ArgumentException("A dialog has one or two choices besides Cancel.", nameof(request));
        }

        var secondaryIndex = request.Choices.Count == 2 ? 1 - request.PrimaryIndex : CancelIndex;
        var dialog = NewDialog(owner, request.Title, request.Body);
        dialog.PrimaryButtonText = request.Choices[request.PrimaryIndex];
        if (secondaryIndex != CancelIndex)
        {
            dialog.SecondaryButtonText = request.Choices[secondaryIndex];
        }

        dialog.CloseButtonText = request.CancelText;
        // A destructive request makes Cancel the Enter default and the focus, so Enter never destroys data.
        dialog.DefaultButton = request.IsDestructive ? ContentDialogButton.Close : ContentDialogButton.Primary;

        var input = request.Input is { } spec ? AddInput(dialog, spec) : null;
        var outcome = await ShowWhenReadyAsync(dialog);
        return outcome switch
        {
            ContentDialogResult.Primary => new DialogResult(request.PrimaryIndex, input?.Text),
            ContentDialogResult.Secondary => new DialogResult(secondaryIndex, input?.Text),
            _ => new DialogResult(CancelIndex, null),
        };
    }

    /// <summary>A confirmation with a primary action and Cancel.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="body">The explanation under the heading.</param>
    /// <param name="primaryText">The primary action's button text.</param>
    /// <param name="destructive">True when the action destroys data: Cancel is then the Enter default.</param>
    /// <returns>True when the owner chose the primary action.</returns>
    public Task<bool> ConfirmAsync(string title, string body, string primaryText, bool destructive = false) =>
        ConfirmAsync(title, body, primaryText, destructive, OwnerWindow());

    internal async Task<bool> ConfirmAsync(string title, string body, string primaryText, bool destructive, Window owner) =>
        !(await ShowAsync(new DialogRequest(title, body, [primaryText]) { IsDestructive = destructive }, owner)).IsCancelled;

    /// <summary>
    /// The window a dialog or file picker opens on: the active window. The steps of one operation keep the
    /// window its first step opened on through a <see cref="BeginFlow">flow</see>.
    /// </summary>
    public Window OwnerWindow() =>
        activeWindow() ?? throw new InvalidOperationException("No window is shown to open a dialog on.");

    /// <summary>Shows a dialog or a file picker on <see cref="OwnerWindow"/>.</summary>
    public Task<T> ShowOnOwnerAsync<T>(Func<Window, Task<T>> show)
    {
        ArgumentNullException.ThrowIfNull(show);
        return show(OwnerWindow());
    }

    /// <summary>Shows a message with an OK button only, and returns when the owner closes it.</summary>
    public async Task ShowMessageAsync(string title, string body)
    {
        var dialog = NewDialog(OwnerWindow(), title, body);
        dialog.CloseButtonText = "OK";
        dialog.DefaultButton = ContentDialogButton.Close;
        await ShowWhenReadyAsync(dialog);
    }

    // UI Automation finds the dialog by its AutomationId only once the focus is in it (its default button or its
    // input box), so a key pressed as soon as it is found (Enter, Esc) reaches it, as with the WPF dialog's modal
    // window. The content dialog shows, and raises Opened, before it moves the focus. GettingFocus is raised
    // synchronously as the focus moves in, so the AutomationId is there by the time anything sees the focus in
    // the dialog (GotFocus is raised asynchronously, after it).
    private Task<ContentDialogResult> ShowWhenReadyAsync(ContentDialog dialog)
    {
        dialog.GettingFocus += (_, args) =>
        {
            if (!args.Cancel)
            {
                AutomationProperties.SetAutomationId(dialog, AutomationId);
            }
        };
        return dialog.ShowAsync().AsTask();
    }

    // The dialog sits in the popup layer, outside the window's content, so it takes the theme the theme service
    // set on that content explicitly.
    private static ContentDialog NewDialog(Window owner, string title, string body)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = owner.Content.XamlRoot,
            RequestedTheme = owner.Content is FrameworkElement root ? root.ActualTheme : ElementTheme.Default,
            Title = title,
            Content = new StackPanel
            {
                Spacing = 16,
                Children = { new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap } },
            },
        };
        if (Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out var style))
        {
            dialog.Style = (Style)style;
        }

        AutomationProperties.SetName(dialog, title);
        return dialog;
    }

    // The box under the body; it takes the focus with its text selected, so typing replaces it.
    private static TextBox AddInput(ContentDialog dialog, DialogInput spec)
    {
        var box = new TextBox { Header = spec.Label, Text = spec.InitialText, MaxLength = spec.MaxLength };
        AutomationProperties.SetName(box, spec.Label);
        ((StackPanel)dialog.Content).Children.Add(box);
        dialog.Opened += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };
        return box;
    }
}
