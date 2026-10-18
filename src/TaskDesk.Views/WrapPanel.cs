using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace TaskDesk.Views;

/// <summary>
/// Lays its children out left to right at their own widths and starts a new line when the next one does not fit
/// (the Filter bar and the detail pane's Tag chips). WinUI has no wrap panel of its own.
/// </summary>
public sealed partial class WrapPanel : Panel
{
    /// <summary>The gap between two children on a line.</summary>
    public double HorizontalSpacing { get; set; }

    /// <summary>The gap between two lines.</summary>
    public double VerticalSpacing { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        }

        return Place(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Place(finalSize.Width, arrange: true);
        return finalSize;
    }

    // Walks the children line by line; returns the size they take and, when asked, arranges them.
    private Size Place(double width, bool arrange)
    {
        double x = 0, y = 0, lineHeight = 0, widest = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (size.Width == 0 && size.Height == 0)
            {
                continue; // collapsed
            }

            if (x > 0 && x + size.Width > width)
            {
                y += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
            }

            if (arrange)
            {
                child.Arrange(new Rect(x, y, size.Width, size.Height));
            }

            widest = Math.Max(widest, x + size.Width);
            lineHeight = Math.Max(lineHeight, size.Height);
            x += size.Width + HorizontalSpacing;
        }

        return new Size(widest, y + lineHeight);
    }
}
