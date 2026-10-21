using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskDesk.Presentation;

namespace TaskDesk.Views;

/// <summary>Picks a Task list row's template by its kind: a Task row or a Subtask row.</summary>
public sealed partial class TaskListRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Task { get; set; }

    public DataTemplate? Subtask { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => Pick(item);

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => Pick(item);

    private DataTemplate? Pick(object item) => item is SubtaskListRowViewModel ? Subtask : Task;
}
