using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using TaskDesk.Presentation;

namespace TaskDesk.Views;

/// <summary>
/// The one Task list's ListView: Fluent's ListView whose row containers let UI Automation read a Task row as
/// expanded or collapsed (<see cref="TaskRowListItem"/>).
/// </summary>
public sealed partial class TaskRowList : ListView
{
    public TaskRowList()
    {
        DefaultStyleKey = typeof(ListView);
    }

    protected override DependencyObject GetContainerForItemOverride() => new TaskRowListItem();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is TaskRowListItem;
}

/// <summary>
/// A row container of the Task list. For a Task with Subtasks, UI Automation's ExpandCollapse pattern reads and
/// changes whether its Subtask rows show.
/// </summary>
public sealed partial class TaskRowListItem : ListViewItem
{
    private TaskRowViewModel? _watched;

    public TaskRowListItem()
    {
        DefaultStyleKey = typeof(ListViewItem);
    }

    /// <summary>Opens (true) or closes (false) a Task: what a screen reader's Expand and Collapse run.</summary>
    public Action<TaskRowViewModel, bool>? SetOpen { get; set; }

    /// <summary>The Task row the container shows, or null for a Subtask row.</summary>
    internal TaskRowViewModel? TaskRow => Content as TaskRowViewModel;

    protected override AutomationPeer OnCreateAutomationPeer() => new TaskRowListItemAutomationPeer(this);

    // A recycled container follows the row it shows now, and lets the previous one go.
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnRowChanged;
        }

        _watched = newContent as TaskRowViewModel;
        if (_watched is not null)
        {
            _watched.PropertyChanged += OnRowChanged;
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskRowViewModel.IsOpen) && sender is TaskRowViewModel row
            && FrameworkElementAutomationPeer.FromElement(this) is TaskRowListItemAutomationPeer peer)
        {
            peer.RaisePropertyChangedEvent(
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                TaskRowListItemAutomationPeer.State(!row.IsOpen), TaskRowListItemAutomationPeer.State(row.IsOpen));
        }
    }
}

/// <summary>A list item peer that also exposes a Task row's open state through ExpandCollapse.</summary>
public sealed partial class TaskRowListItemAutomationPeer(TaskRowListItem owner)
    : ListViewItemAutomationPeer(owner), IExpandCollapseProvider
{
    public ExpandCollapseState ExpandCollapseState => owner.TaskRow is { HasSubtasks: true } row
        ? State(row.IsOpen)
        : ExpandCollapseState.LeafNode;

    public void Expand()
    {
        if (owner.TaskRow is { } row)
        {
            owner.SetOpen?.Invoke(row, true);
        }
    }

    public void Collapse()
    {
        if (owner.TaskRow is { } row)
        {
            owner.SetOpen?.Invoke(row, false);
        }
    }

    internal static ExpandCollapseState State(bool isOpen) =>
        isOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    // Only a Task row with Subtasks expands; a Subtask row and a Task without Subtasks have no such pattern.
    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.ExpandCollapse && owner.TaskRow is { HasSubtasks: true }
            ? this
            : base.GetPatternCore(patternInterface);
}
