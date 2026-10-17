using System.Windows;

namespace TaskDesk.App.Dialogs;

/// <summary>An optional text box in a dialog, such as the new name in Rename List.</summary>
/// <param name="Label">The visible label above the box; also its accessible name.</param>
/// <param name="InitialText">The text the box starts with.</param>
/// <param name="MaxLength">The most characters the box accepts.</param>
internal sealed record FluentDialogInput(string Label, string InitialText, int MaxLength = 100);

/// <summary>What a shared Fluent dialog shows.</summary>
/// <param name="Title">The window title and heading.</param>
/// <param name="Body">The explanation under the heading.</param>
/// <param name="Choices">The action buttons. One is the accent primary button; Cancel is always added.</param>
internal sealed record FluentDialogRequest(string Title, string Body, IReadOnlyList<string> Choices)
{
    /// <summary>
    /// Which choice is the accent primary button, shown on the right and the Enter default unless the request
    /// <see cref="IsDestructive"/>.
    /// </summary>
    public int PrimaryIndex { get; init; }

    /// <summary>The Cancel button's text.</summary>
    public string CancelText { get; init; } = "Cancel";

    /// <summary>
    /// True when the primary action destroys data (Delete list, Replace all data): Cancel is then the Enter
    /// default and has the focus, and the primary button needs a click or Tab.
    /// </summary>
    public bool IsDestructive { get; init; }

    /// <summary>A text box under the body, or null for none.</summary>
    public FluentDialogInput? Input { get; init; }
}

/// <summary>The outcome of a shared Fluent dialog.</summary>
/// <param name="ChoiceIndex">The index in the request's choices, or <see cref="FluentDialogService.CancelIndex"/>.</param>
/// <param name="Text">The input box text, or null when the request had no input.</param>
internal sealed record FluentDialogResult(int ChoiceIndex, string? Text)
{
    /// <summary>True when the owner cancelled with Cancel or Esc.</summary>
    public bool IsCancelled => ChoiceIndex == FluentDialogService.CancelIndex;
}

/// <summary>Shows the one shared Fluent dialog, centred on the window the owner is using.</summary>
internal static class FluentDialogService
{
    /// <summary>The <see cref="FluentDialogResult.ChoiceIndex"/> of Cancel and Esc.</summary>
    public const int CancelIndex = -1;

    /// <summary>Shows the dialog modally and returns what the owner chose.</summary>
    public static FluentDialogResult Show(FluentDialogRequest request, Window? owner = null)
    {
        var dialog = new FluentDialog(request);
        owner ??= ActiveWindow();
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true
            ? new FluentDialogResult(dialog.ChosenIndex, dialog.InputText)
            : new FluentDialogResult(CancelIndex, null);
    }

    /// <summary>A confirmation with a primary action and Cancel.</summary>
    /// <param name="title">The window title and heading.</param>
    /// <param name="body">The explanation under the heading.</param>
    /// <param name="primaryText">The primary action's button text.</param>
    /// <param name="destructive">True when the action destroys data: Cancel is then the Enter default.</param>
    /// <param name="owner">The window to centre on, or null for the active one.</param>
    /// <returns>True when the owner chose the primary action.</returns>
    public static bool Confirm(
        string title, string body, string primaryText, bool destructive = false, Window? owner = null) =>
        !Show(new FluentDialogRequest(title, body, [primaryText]) { IsDestructive = destructive }, owner).IsCancelled;

    private static Window? ActiveWindow() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
}
