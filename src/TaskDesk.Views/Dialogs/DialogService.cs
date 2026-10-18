using Microsoft.UI.Dispatching;
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
    // The window of the dialog flow under way, and which dialog of it opened last.
    private Window? _flowOwner;
    private int _flowGeneration;

    /// <summary>The <see cref="DialogResult.ChoiceIndex"/> of Cancel and Esc.</summary>
    public const int CancelIndex = -1;

    /// <summary>The AutomationId the UI tests find a dialog by, kept from the WPF Fluent dialog.</summary>
    public const string AutomationId = "FluentDialog";

    /// <summary>Shows the dialog and returns what the owner chose.</summary>
    public async Task<DialogResult> ShowAsync(DialogRequest request)
    {
        // A content dialog has three buttons: the primary choice, at most one other, and Cancel.
        if (request.Choices.Count is < 1 or > 2)
        {
            throw new ArgumentException("A dialog has one or two choices besides Cancel.", nameof(request));
        }

        var secondaryIndex = request.Choices.Count == 2 ? 1 - request.PrimaryIndex : CancelIndex;
        var owner = OwnerWindow();
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
    public async Task<bool> ConfirmAsync(string title, string body, string primaryText, bool destructive = false) =>
        !(await ShowAsync(new DialogRequest(title, body, [primaryText]) { IsDestructive = destructive })).IsCancelled;

    /// <summary>
    /// The window dialogs and file pickers open on: the active window, except while a flow of dialogs runs
    /// (see <see cref="ShowOnOwnerAsync{T}"/>), which keeps the window its first dialog opened on.
    /// </summary>
    public Window OwnerWindow() =>
        _flowOwner ?? activeWindow() ?? throw new InvalidOperationException("No window is shown to open a dialog on.");

    /// <summary>
    /// Shows a dialog or a file picker on <see cref="OwnerWindow"/>. A dialog or picker opened as soon as this
    /// one closes (the next step of the same operation, such as Import's file, then its Merge/Replace choice,
    /// then the Replace confirmation) opens on the same window, even if the owner activated another one meanwhile.
    /// </summary>
    public async Task<T> ShowOnOwnerAsync<T>(Func<Window, Task<T>> show)
    {
        ArgumentNullException.ThrowIfNull(show);
        var owner = OwnerWindow();
        _flowOwner = owner;
        var generation = ++_flowGeneration;
        try
        {
            return await show(owner);
        }
        finally
        {
            // The operation's next step runs before the dispatcher turns to low-priority work, so the flow ends
            // when no other dialog has opened by then.
            if (owner.DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, () => EndFlow(generation)) != true)
            {
                EndFlow(generation);
            }
        }
    }

    private void EndFlow(int generation)
    {
        if (generation == _flowGeneration)
        {
            _flowOwner = null;
        }
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
    // window. The content dialog shows, and raises Opened, before it moves the focus.
    private Task<ContentDialogResult> ShowWhenReadyAsync(ContentDialog dialog)
    {
        dialog.GotFocus += (_, _) => AutomationProperties.SetAutomationId(dialog, AutomationId);
        return ShowOnOwnerAsync(_ => dialog.ShowAsync().AsTask());
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
