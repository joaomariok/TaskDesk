using Microsoft.UI.Xaml.Controls;

namespace TaskDesk.Views;

/// <summary>
/// The scrolling side of a <see cref="ReorderDrag"/> host whose list scrolls in a <see cref="ScrollViewer"/>: scrolls
/// it for the drag's edge scroll, and tells the drag when it scrolled, so the insertion line keeps to the gap under
/// the pointer.
/// </summary>
public sealed class DragScroller(ReorderDrag drag)
{
    private ScrollViewer? _scroll;
    private double? _target;

    /// <summary>The scroll area to scroll; the first one attached stays.</summary>
    public void Attach(ScrollViewer scroll)
    {
        if (_scroll is not null)
        {
            return;
        }

        _scroll = scroll;
        scroll.ViewChanged += OnViewChanged;
    }

    /// <summary>
    /// Scrolls by <paramref name="dy"/> DIPs (negative is up), stopping at the ends. Returns false when it is
    /// already at the end it would scroll towards, or when nothing is attached.
    /// </summary>
    public bool ScrollBy(double dy)
    {
        if (_scroll is not { } scroll)
        {
            return false;
        }

        // A step before the last one has landed goes on from where that one is headed, so no step is lost.
        var from = _target ?? scroll.VerticalOffset;
        var target = Math.Clamp(from + dy, 0, scroll.ScrollableHeight);
        if (target == from || !scroll.ChangeView(null, target, null, disableAnimation: true))
        {
            return false;
        }

        _target = target;
        return true;
    }

    /// <summary>The drag ended: a scroll step it left in flight no longer counts.</summary>
    public void Stop() => _target = null;

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!e.IsIntermediate)
        {
            _target = null;
        }

        drag.ListScrolled();
    }
}
