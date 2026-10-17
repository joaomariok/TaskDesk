using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace TaskDesk.App.Dialogs;

/// <summary>The shared Fluent dialog window. Build it through <see cref="FluentDialogService"/>.</summary>
internal sealed partial class FluentDialog : Window
{
    internal FluentDialog(FluentDialogRequest request)
    {
        InitializeComponent();
        Title = request.Title;
        HeadingText.Text = request.Title;
        BodyTextBlock.Text = request.Body;
        AutomationProperties.SetName(this, request.Title);

        if (request.Input is { } input)
        {
            InputPanel.Visibility = Visibility.Visible;
            InputLabelText.Text = input.Label;
            InputBox.Text = input.InitialText;
            InputBox.MaxLength = input.MaxLength;
            AutomationProperties.SetName(InputBox, input.Label);
            Loaded += (_, _) =>
            {
                InputBox.Focus();
                InputBox.SelectAll();
            };
        }

        // Left to right: the other choices, Cancel, then the accent primary button on the right.
        for (var i = 0; i < request.Choices.Count; i++)
        {
            if (i != request.PrimaryIndex)
            {
                ButtonRow.Children.Add(MakeButton(request.Choices[i], i, isPrimary: false));
            }
        }

        ButtonRow.Children.Add(MakeCancel(request.CancelText));
        var primaryButton = MakeButton(request.Choices[request.PrimaryIndex], request.PrimaryIndex, isPrimary: true);
        ButtonRow.Children.Add(primaryButton);
        if (request.Input is null)
        {
            Loaded += (_, _) => primaryButton.Focus();
        }
    }

    /// <summary>The index of the chosen choice, or <see cref="FluentDialogService.CancelIndex"/> for Cancel and Esc.</summary>
    internal int ChosenIndex { get; private set; } = FluentDialogService.CancelIndex;

    /// <summary>The text in the input box, or null when the dialog has no input.</summary>
    internal string? InputText => InputPanel.Visibility == Visibility.Visible ? InputBox.Text : null;

    private static Button MakeCancel(string text)
    {
        var button = new Button { Content = text, IsCancel = true, MinWidth = 96, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(button, text);
        return button;
    }

    private Button MakeButton(string text, int index, bool isPrimary)
    {
        var button = new Button
        {
            Content = text,
            IsDefault = isPrimary,
            MinWidth = 96,
            Margin = new Thickness(8, 0, 0, 0),
        };
        if (isPrimary)
        {
            button.SetResourceReference(StyleProperty, "AccentButtonStyle");
        }

        AutomationProperties.SetName(button, text);
        button.Click += (_, _) =>
        {
            ChosenIndex = index;
            DialogResult = true;
        };
        return button;
    }
}
