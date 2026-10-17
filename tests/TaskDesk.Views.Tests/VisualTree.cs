using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace TaskDesk.Views.Tests;

/// <summary>Walks the live visual tree; templates apply only once an element is in a shown window.</summary>
public static class VisualTree
{
    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    public static T Find<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants(root).OfType<T>().FirstOrDefault(e => e.Name == name)
        ?? throw new InvalidOperationException($"No {typeof(T).Name} named '{name}'.");
}
