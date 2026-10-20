using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskDesk.Presentation;

namespace TaskDesk.Views;

/// <summary>Picks a Task list group's heading template by the group's kind.</summary>
public sealed partial class TaskGroupHeadingSelector : DataTemplateSelector
{
    public DataTemplate? Overdue { get; set; }

    public DataTemplate? Open { get; set; }

    public DataTemplate? Completed { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => Pick(item);

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => Pick(item);

    private DataTemplate? Pick(object item) => (item as TaskGroup)?.Kind switch
    {
        TaskGroupKind.Overdue => Overdue,
        TaskGroupKind.Completed => Completed,
        _ => Open,
    };
}
