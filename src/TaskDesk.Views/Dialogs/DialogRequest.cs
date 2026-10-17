namespace TaskDesk.Views.Dialogs;

/// <summary>An optional text box in a dialog, such as the new name in Rename List.</summary>
/// <param name="Label">The visible label above the box; also its accessible name.</param>
/// <param name="InitialText">The text the box starts with.</param>
/// <param name="MaxLength">The most characters the box accepts.</param>
public sealed record DialogInput(string Label, string InitialText, int MaxLength = 100);

/// <summary>What a dialog shows: the WinUI counterpart of the WPF app's shared Fluent dialog request.</summary>
/// <param name="Title">The heading.</param>
/// <param name="Body">The explanation under the heading.</param>
/// <param name="Choices">
/// The action buttons, one or two. One is the primary button; Cancel is always added.
/// </param>
public sealed record DialogRequest(string Title, string Body, IReadOnlyList<string> Choices)
{
    /// <summary>Which choice is the primary button, the Enter default unless the request <see cref="IsDestructive"/>.</summary>
    public int PrimaryIndex { get; init; }

    /// <summary>The Cancel button's text.</summary>
    public string CancelText { get; init; } = "Cancel";

    /// <summary>
    /// True when the primary action destroys data (Delete list, Replace all data): Cancel is then the Enter
    /// default and has the focus, and the primary button needs a click or Tab.
    /// </summary>
    public bool IsDestructive { get; init; }

    /// <summary>A text box under the body, or null for none.</summary>
    public DialogInput? Input { get; init; }
}

/// <summary>The outcome of a dialog.</summary>
/// <param name="ChoiceIndex">The index in the request's choices, or <see cref="DialogService.CancelIndex"/>.</param>
/// <param name="Text">The input box text, or null when the request had no input.</param>
public sealed record DialogResult(int ChoiceIndex, string? Text)
{
    /// <summary>True when the owner cancelled with Cancel or Esc.</summary>
    public bool IsCancelled => ChoiceIndex == DialogService.CancelIndex;
}
