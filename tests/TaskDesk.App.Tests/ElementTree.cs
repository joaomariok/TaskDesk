using System.Windows;
using System.Windows.Media;

namespace TaskDesk.App.Tests;

/// <summary>Walks a laid-out element for the App tests.</summary>
internal static class ElementTree
{
    /// <summary>
    /// The root and everything under it, in the visual and the logical tree together, so nothing is missed
    /// whether layout reached it or not.
    /// </summary>
    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var pending = new Stack<DependencyObject>([root]);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node))
            {
                continue;
            }

            yield return node;
            if (node is Visual)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                {
                    pending.Push(VisualTreeHelper.GetChild(node, i));
                }
            }

            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                pending.Push(child);
            }
        }
    }
}
