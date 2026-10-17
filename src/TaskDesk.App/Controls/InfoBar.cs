using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TaskDesk.App.Controls;

/// <summary>How serious an <see cref="InfoBar"/> message is; it picks the icon and the colours.</summary>
public enum InfoBarSeverity
{
    /// <summary>Neutral feedback, such as "Task deleted".</summary>
    Informational,

    /// <summary>Something the user should know is not working.</summary>
    Warning,
}

/// <summary>
/// A Fluent InfoBar: a severity icon, a message (the content), an optional action button and an
/// optional close button. Styled in <c>Styles/InfoBar.xaml</c> from Fluent's resources.
/// </summary>
/// <remarks>
/// The bar shows or hides through its own <see cref="UIElement.Visibility"/>; give it an
/// <c>AutomationProperties.Name</c> so a screen reader announces what it is.
/// </remarks>
public sealed class InfoBar : ContentControl
{
    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity), typeof(InfoBarSeverity), typeof(InfoBar), new PropertyMetadata(InfoBarSeverity.Informational));

    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(
        nameof(ActionContent), typeof(object), typeof(InfoBar), new PropertyMetadata(null));

    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(
        nameof(ActionCommand), typeof(ICommand), typeof(InfoBar), new PropertyMetadata(null));

    public static readonly DependencyProperty ActionAutomationIdProperty = DependencyProperty.Register(
        nameof(ActionAutomationId), typeof(string), typeof(InfoBar), new PropertyMetadata(null));

    public static readonly DependencyProperty IsClosableProperty = DependencyProperty.Register(
        nameof(IsClosable), typeof(bool), typeof(InfoBar), new PropertyMetadata(false));

    public static readonly DependencyProperty CloseCommandProperty = DependencyProperty.Register(
        nameof(CloseCommand), typeof(ICommand), typeof(InfoBar), new PropertyMetadata(null));

    static InfoBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(InfoBar), new FrameworkPropertyMetadata(typeof(InfoBar)));
    }

    public InfoBarSeverity Severity
    {
        get => (InfoBarSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    /// <summary>The action button's content; the button shows only when this is set.</summary>
    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    /// <summary>The action button's <c>AutomationId</c>, for UI tests.</summary>
    public string? ActionAutomationId
    {
        get => (string?)GetValue(ActionAutomationIdProperty);
        set => SetValue(ActionAutomationIdProperty, value);
    }

    /// <summary>Shows the close button, which runs <see cref="CloseCommand"/>.</summary>
    public bool IsClosable
    {
        get => (bool)GetValue(IsClosableProperty);
        set => SetValue(IsClosableProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }
}
