using System.Windows;
using System.Windows.Controls;

namespace TaskDesk.App.Controls;

/// <summary>
/// A Windows 11 Settings-style card: a title and a one-line description on the left, and the setting's
/// control (the content) on the right. Styled in <c>Styles/SettingsCard.xaml</c> from Fluent's resources.
/// </summary>
/// <remarks>
/// The card itself is not focusable; the control inside it takes focus, so give that control an
/// <c>AutomationProperties.Name</c>.
/// </remarks>
public sealed class SettingsCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata(null));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata(null));

    static SettingsCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingsCard), new FrameworkPropertyMetadata(typeof(SettingsCard)));
    }

    /// <summary>The card's title.</summary>
    public string? Header
    {
        get => (string?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>The one-line description under the title; hidden when not set.</summary>
    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
