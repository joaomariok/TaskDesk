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

    /// <summary>Shows the dialog and returns what the owner chose.</summary>
    public async Task<DialogResult> ShowAsync(DialogRequest request)
    {
        // A content dialog has three buttons: the primary choice, at most one other, and Cancel.
        if (request.Choices.Count is < 1 or > 2)
        {
            throw new ArgumentException("A dialog has one or two choices besides Cancel.", nameof(request));
        }

        var secondaryIndex = request.Choices.Count == 2 ? 1 - request.PrimaryIndex : CancelIndex;
        var dialog = NewDialog(request.Title, request.Body);
        dialog.PrimaryButtonText = request.Choices[request.PrimaryIndex];
        if (secondaryIndex != CancelIndex)
        {
            dialog.SecondaryButtonText = request.Choices[secondaryIndex];
        }

        dialog.CloseButtonText = request.CancelText;
        // A destructive request makes Cancel the Enter default and the focus, so Enter never destroys data.
        dialog.DefaultButton = request.IsDestructive ? ContentDialogButton.Close : ContentDialogButton.Primary;

        var input = request.Input is { } spec ? AddInput(dialog, spec) : null;
        var outcome = await dialog.ShowAsync();
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

    /// <summary>The window dialogs and file pickers open on.</summary>
    public Window OwnerWindow() =>
        activeWindow() ?? throw new InvalidOperationException("No window is shown to open a dialog on.");

    /// <summary>Shows a message with an OK button only, and returns when the owner closes it.</summary>
    public async Task ShowMessageAsync(string title, string body)
    {
        var dialog = NewDialog(title, body);
        dialog.CloseButtonText = "OK";
        dialog.DefaultButton = ContentDialogButton.Close;
        await dialog.ShowAsync();
    }

    private ContentDialog NewDialog(string title, string body)
    {
        var window = OwnerWindow();
        var dialog = new ContentDialog
        {
            XamlRoot = window.Content.XamlRoot,
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

        AutomationProperties.SetAutomationId(dialog, AutomationId);
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
