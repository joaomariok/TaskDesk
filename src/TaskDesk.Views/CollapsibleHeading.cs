using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace TaskDesk.Views;

/// <summary>
/// A group heading that expands and collapses its group: a click, Enter or Space flips <see cref="IsExpanded"/>.
/// UI Automation reads it as a button with the ExpandCollapse pattern, so a screen reader announces whether the
/// group is expanded. It draws Fluent's Button style.
/// </summary>
public sealed partial class CollapsibleHeading : Button
{
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(CollapsibleHeading),
        new PropertyMetadata(false, OnIsExpandedChanged));

    public CollapsibleHeading()
    {
        DefaultStyleKey = typeof(Button);
        Click += (_, _) => IsExpanded = !IsExpanded;
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>The chevron glyph: down while expanded, right while collapsed.</summary>
    public static string Chevron(bool isExpanded) => isExpanded ? "" : "";

    protected override AutomationPeer OnCreateAutomationPeer() => new CollapsibleHeadingAutomationPeer(this);

    private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (FrameworkElementAutomationPeer.FromElement((UIElement)d) is CollapsibleHeadingAutomationPeer peer)
        {
            peer.RaisePropertyChangedEvent(
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                State((bool)e.OldValue), State((bool)e.NewValue));
        }
    }

    internal static ExpandCollapseState State(bool isExpanded) =>
        isExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
}

/// <summary>A button peer that also exposes the heading's expanded state through ExpandCollapse.</summary>
public sealed partial class CollapsibleHeadingAutomationPeer(CollapsibleHeading owner)
    : ButtonAutomationPeer(owner), IExpandCollapseProvider
{
    public ExpandCollapseState ExpandCollapseState => CollapsibleHeading.State(owner.IsExpanded);

    public void Expand() => owner.IsExpanded = true;

    public void Collapse() => owner.IsExpanded = false;

    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPatternCore(patternInterface);
}
