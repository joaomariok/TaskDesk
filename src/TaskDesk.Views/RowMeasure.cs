using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskDesk.Core.Shell;

namespace TaskDesk.Views;

// Measures the realised rows of a list for ReorderGeometry, shared by the sidebar's List drag and the Task drag.
internal static class RowMeasure
{
    /// <summary>The span of each item of <paramref name="list"/>, null for an item with no container.</summary>
    public static RowSpan?[] Spans(ListViewBase list)
    {
        var spans = new RowSpan?[list.Items.Count];
        for (var i = 0; i < spans.Length; i++)
        {
            if (list.ContainerFromIndex(i) is FrameworkElement container)
            {
                var top = container.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                spans[i] = new RowSpan(top, container.ActualHeight);
            }
        }

        return spans;
    }
}
