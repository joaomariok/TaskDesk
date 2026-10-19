using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace TaskDesk.Views;

/// <summary>Walks a visual tree, for code that must reach a control's template parts.</summary>
internal static class VisualTreeWalk
{
    /// <summary>Every descendant of <paramref name="root"/>, depth first, each before its own children; not the root.</summary>
    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
