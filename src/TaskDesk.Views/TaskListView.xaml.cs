using System.ComponentModel;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using Windows.Foundation.Collections;
using Windows.System;
using Windows.UI.ViewManagement;

namespace TaskDesk.Views;

/// <summary>
/// The Task list area: one list over the page's groups (Overdue, open, "Completed (N)"), its keyboard shortcuts
/// and the Undo bar. The list's one selection is the view model's selected Task, both ways.
/// </summary>
public sealed partial class TaskListView : UserControl, IReorderDragHost
{
    private ScrollViewer? _listScroll;
    private double? _edgeScrollTarget;

    public TaskListView(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        // A completed Task lingers struck through for a moment, unless Windows asks for less animation.
        viewModel.CompletionMoveDelay = () => CompletionHold.For(AnimationsEnabled());
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        // With no row in any group (an empty page) the list takes no focus, so Tab skips it.
        OpenTaskList.IsTabStop = OpenTaskList.Items.Count > 0;
        OpenTaskList.Items.VectorChanged += OnRowsChanged;
        OpenTaskList.Tapped += OnTaskListTapped;

        // The row containers handle the press and the release themselves, so the drag listens to handled ones too.
        TaskDrag = new ReorderDrag(this, viewModel.Clock);
        OpenTaskList.AddHandler(PointerPressedEvent, new PointerEventHandler(OnDragPointerPressed), true);
        OpenTaskList.AddHandler(PointerMovedEvent, new PointerEventHandler(OnDragPointerMoved), true);
        OpenTaskList.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnDragPointerReleased), true);
        OpenTaskList.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnDragPointerCaptureLost), true);
        OpenTaskList.Loaded += OnTaskListLoaded;
        // A touch swipe that the list's scrolling takes over cancels the pointer: that press never drags.
        OpenTaskList.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnDragPointerCaptureLost), true);
        // A reload replaces the rows, and a deleted or completed Task leaves them: either way its drag ends.
        viewModel.Tasks.CollectionChanged += (_, _) => CancelDragOfGoneTask();
    }

    public MainViewModel ViewModel { get; }

    /// <summary>The drag that reorders the open Tasks (ADR 0007), fed by the list's pointer events.</summary>
    public ReorderDrag TaskDrag { get; }

    /// <summary>Windows' "Animation effects" setting; replaceable so tests need not depend on the machine.</summary>
    public Func<bool> AnimationsEnabled { get; set; } = () => new UISettings().AnimationsEnabled;

    /// <summary>
    /// Runs the Task list shortcut for a key: Space completes and Delete deletes the selected Task, Enter opens it
    /// in the detail pane, Right opens and Left closes its Subtask rows, Ctrl+Z undoes the last delete or
    /// completion. On a selected Subtask row, Enter opens its Task, Left selects the Task, Space ticks or unticks
    /// the Subtask, and Delete does nothing. While a Task is dragged, Esc cancels the drag. Returns false when the
    /// key is not a shortcut or there is nothing to do.
    /// </summary>
    public bool TryShortcut(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        if ((key, modifiers) == (VirtualKey.Escape, VirtualKeyModifiers.None) && TaskDrag.IsDragging)
        {
            TaskDrag.Cancel();
            return true;
        }

        // Enter opens the detail pane on the selected Task and moves focus into it.
        if ((key, modifiers) == (VirtualKey.Enter, VirtualKeyModifiers.None))
        {
            if (ViewModel.SelectedTask is not { } selected)
            {
                return false;
            }

            ViewModel.OpenDetailPane(selected);
            DetailFocusRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        // Left on a Subtask row selects its Task; Right does nothing there.
        if (modifiers == VirtualKeyModifiers.None && key is VirtualKey.Right or VirtualKey.Left
            && ViewModel.SelectedSubtaskRow is { } subtask)
        {
            if (key == VirtualKey.Right)
            {
                return false;
            }

            FocusRow(subtask.TaskRow, keepSubtaskRow: false);
            return true;
        }

        // Right opens the selected Task's Subtask rows and Left closes them.
        if (modifiers == VirtualKeyModifiers.None && key is VirtualKey.Right or VirtualKey.Left
            && ViewModel.SelectedTask is { HasSubtasks: true } task)
        {
            var open = key == VirtualKey.Right;
            if (task.IsOpen == open)
            {
                return false;
            }

            SetTaskOpen(task, open);
            return true;
        }

        ICommand? command = (key, modifiers) switch
        {
            (VirtualKey.Space, VirtualKeyModifiers.None) => ViewModel.ToggleSelectedCompleteCommand,
            (VirtualKey.Delete, VirtualKeyModifiers.None) => ViewModel.DeleteSelectedCommand,
            (VirtualKey.Z, VirtualKeyModifiers.Control) => ViewModel.UndoCommand,
            _ => null,
        };
        if (command is null || !command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    /// <summary>Raised when Enter opened the detail pane on a row and focus should move into the pane.</summary>
    public event EventHandler? DetailFocusRequested;

    /// <summary>
    /// Selects a Task's row and gives it keyboard focus (after Esc closed the detail pane). While one of the Task's
    /// Subtask rows is selected, that row keeps the selection and takes the focus.
    /// </summary>
    public void FocusRow(TaskRowViewModel row) => FocusRow(row, keepSubtaskRow: true);

    private void FocusRow(TaskRowViewModel row, bool keepSubtaskRow)
    {
        ITaskListRow target = keepSubtaskRow && ViewModel.SelectedSubtaskRow is { } subtask && subtask.TaskRow == row
            ? subtask
            : row;
        var list = OpenTaskList;
        if (!list.Items.Contains(target))
        {
            return;
        }

        ViewModel.SelectedRow = target;
        list.SelectedItem = target;
        list.ScrollIntoView(target);
        list.UpdateLayout();
        (list.ContainerFromItem(target) as ListViewItem)?.Focus(FocusState.Keyboard);
    }

    // A click on a row opens the detail pane on it, a click on a Subtask row on its Task (arrow keys only move the
    // selection); a click on the row's checkbox or chevron does neither, and nor does the release of a drag.
    private void OnTaskListTapped(object sender, TappedRoutedEventArgs e)
    {
        if (!TaskDrag.LastPressDragged)
        {
            OpenRowAt(e.OriginalSource as DependencyObject);
        }
    }

    /// <summary>
    /// What a click on <paramref name="element"/> inside the list does: on a row, opens the detail pane on its Task
    /// and returns true; on a row's checkbox or chevron, or outside a row, does nothing and returns false.
    /// </summary>
    public bool OpenRowAt(DependencyObject? element)
    {
        for (; element is not null and not ListView; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase)
            {
                return false;
            }

            if (element is ListViewItem { Content: ITaskListRow row })
            {
                ViewModel.SelectedRow = row;
                ViewModel.OpenDetailPane(row.TaskRow);
                return true;
            }
        }

        return false;
    }

    // Space and Delete act on a focused row. A row's own checkbox handles Space itself. Esc cancels a drag
    // wherever the focus is in the list.
    private void OnTaskListPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if ((e.OriginalSource is ListViewItem || (e.Key == VirtualKey.Escape && TaskDrag.IsDragging))
            && TryShortcut(e.Key, KeyboardModifiers.Current()))
        {
            e.Handled = true;
        }
    }

    // Ctrl+Z anywhere in the window, except in a text box, which undoes its own typing.
    private void OnUndoInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (XamlRoot is { } root && FocusManager.GetFocusedElement(root) is TextBox)
        {
            return;
        }

        args.Handled = TryShortcut(VirtualKey.Z, VirtualKeyModifiers.Control);
    }

    // UI Automation names a row by its Task title (the UI tests find rows by name); the row's checkbox runs
    // the view model's toggle.
    private void OnRowContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is SubtaskListRowViewModel subtask)
        {
            AutomationProperties.SetName(args.ItemContainer, subtask.AccessibleName);
            if (args.ItemContainer.ContentTemplateRoot is SubtaskRow subtaskView)
            {
                subtaskView.ToggleCommand = ViewModel.ToggleSubtaskCommand;
            }

            return;
        }

        if (args.Item is not TaskRowViewModel row)
        {
            return;
        }

        AutomationProperties.SetName(args.ItemContainer, row.Title);
        if (args.ItemContainer is TaskRowListItem item)
        {
            item.SetOpen ??= SetTaskOpen;
        }

        if (args.ItemContainer.ContentTemplateRoot is TaskRow view)
        {
            view.ToggleCompleteCommand = ViewModel.ToggleCompleteCommand;
            view.ToggleOpenCommand = ViewModel.ToggleTaskOpenCommand;
        }
    }

    private void SetTaskOpen(TaskRowViewModel row, bool open)
    {
        if (open)
        {
            ViewModel.OpenTask(row);
        }
        else
        {
            ViewModel.CloseTask(row);
        }
    }

    private void OnTaskSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A Task row, or a Subtask row, which selects its Task too. A selection that only lost its row comes from the
        // row moving between groups or hiding: the view model keeps the selection, and the list selects the row
        // again where it shows.
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ITaskListRow row)
        {
            ViewModel.SelectedRow = row;
        }
    }

    // Shows the view model's selection (for example after a reload) on its row, or none while the row is hidden
    // (in a collapsed "Completed (N)").
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Page))
        {
            TaskDrag.Cancel(); // a drag never carries over to another page
        }

        if (e.PropertyName == nameof(MainViewModel.SelectedRow))
        {
            var left = _shownRow;
            ShowSelection();
            FocusTaskAfterItsSubtaskRowWent(left);
        }
    }

    // The row the list last showed as the view model's selection.
    private ITaskListRow? _shownRow;

    private void ShowSelection()
    {
        var row = ViewModel.SelectedRow;
        _shownRow = row;
        var shown = row is not null && OpenTaskList.Items.Contains(row) ? row : null;
        if (!ReferenceEquals(OpenTaskList.SelectedItem, shown))
        {
            OpenTaskList.SelectedItem = shown;
        }
    }

    // A selected Subtask row handed the selection to its Task: its Task is closing and the row is about to leave the
    // list (the focus may still be on it, or inside it), or its Subtask was deleted and the row has left. A keyboard
    // focus it held goes to the Task's row once the rows have changed, not to whichever row took its place.
    private void FocusTaskAfterItsSubtaskRowWent(ITaskListRow? left)
    {
        if (left is not SubtaskListRowViewModel subtask || ViewModel.SelectedRow != subtask.TaskRow
            || XamlRoot is not { } root || RowContainerOf(FocusManager.GetFocusedElement(root)) is not { } focused
            || (OpenTaskList.ItemFromContainer(focused) != subtask && OpenTaskList.Items.Contains(subtask)))
        {
            return;
        }

        var task = subtask.TaskRow;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel.SelectedRow == task)
            {
                FocusRow(task, keepSubtaskRow: false);
            }
        });
    }

    // The Task list's row container that holds the focused element, the container itself or a control inside it
    // (a row's checkbox or chevron); null when the focus is elsewhere.
    private ListViewItem? RowContainerOf(object? focused)
    {
        var element = focused as DependencyObject;
        for (; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ListViewItem item)
            {
                return ItemsControl.ItemsControlFromItemContainer(item) == OpenTaskList ? item : null;
            }
        }

        return null;
    }

    private void OnRowsChanged(IObservableVector<object> sender, IVectorChangedEventArgs e)
    {
        OpenTaskList.IsTabStop = OpenTaskList.Items.Count > 0;

        // A row that moved between groups left the list for a moment and lost its selection; when it is inserted
        // again, select it once the list has taken the change in. Only the inserted item is looked at, so a long
        // load stays linear.
        if (e.CollectionChange == CollectionChange.ItemInserted && ViewModel.SelectedRow is { } row
            && ReferenceEquals(sender[(int)e.Index], row) && !ReferenceEquals(OpenTaskList.SelectedItem, row))
        {
            DispatcherQueue.TryEnqueue(ShowSelection);
        }
    }

    // ---- Drag to reorder (the open rows, in a List view only) ----

    /// <summary>
    /// The open Task row under <paramref name="position"/> (relative to the list), or null over empty space, a
    /// heading or a row of another group.
    /// </summary>
    public TaskRowViewModel? RowAt(Windows.Foundation.Point position)
    {
        var items = OpenTaskList.Items;
        var spans = RowMeasure.Spans(OpenTaskList);
        for (var i = 0; i < spans.Length; i++)
        {
            if (items[i] is not TaskRowViewModel row || !ViewModel.Tasks.Contains(row))
            {
                spans[i] = null;
            }
        }

        var index = ReorderGeometry.RowAt(spans, position.Y);
        return index is { } at ? (TaskRowViewModel)items[at] : null;
    }

    /// <summary>
    /// Drops <paramref name="dragged"/> on <paramref name="target"/>, or on empty space (null) to move it to the
    /// end, through the view model's move use case. Does nothing where reordering is off.
    /// </summary>
    public async Task DropAsync(TaskRowViewModel dragged, TaskRowViewModel? target)
    {
        if (!ViewModel.IsReorderEnabled)
        {
            return;
        }

        var oldIndex = ViewModel.Tasks.IndexOf(dragged);
        var newIndex = target is null ? ViewModel.Tasks.Count - 1 : ViewModel.Tasks.IndexOf(target);
        await ViewModel.MoveTaskAsync(oldIndex, newIndex);
    }

    /// <summary>
    /// The spans of the open group's rows (each open Task row, then its Subtask rows while it is open), in order;
    /// null for a row with no container.
    /// </summary>
    private List<RowSpan?> OpenRowSpans()
    {
        var all = RowMeasure.Spans(OpenTaskList);
        var spans = new List<RowSpan?>();
        for (var i = 0; i < all.Length; i++)
        {
            if (OpenTaskList.Items[i] is ITaskListRow row && ViewModel.Tasks.Contains(row.TaskRow))
            {
                spans.Add(all[i]);
            }
        }

        return spans;
    }

    /// <summary>
    /// The gap over the open Tasks a drop at <paramref name="position"/> (relative to the list) lands in: over a
    /// row's top half the gap before it, over its bottom half the gap after it, and below the rows the last gap.
    /// A pointer inside an open Task's block (its Task row's bottom half or a Subtask row) is the gap after it.
    /// </summary>
    public int GapAt(Windows.Foundation.Point position) =>
        ViewModel.TaskGapOfRowGap(ReorderGeometry.GapAt(OpenRowSpans(), position.Y));

    /// <summary>
    /// Drops <paramref name="dragged"/> into the gap <paramref name="gap"/> over the open rows through the view
    /// model's move use case, and hides the insertion line. Does nothing where reordering is off.
    /// </summary>
    public async Task DropIntoGapAsync(TaskRowViewModel dragged, int gap)
    {
        HideInsertionLine();
        if (ViewModel.IsReorderEnabled)
        {
            await ViewModel.MoveTaskToGapAsync(dragged, gap);
        }
    }

    /// <summary>
    /// Shows the insertion line on the gap <paramref name="gap"/> over the open Tasks while a Task is dragged: at
    /// the top of the row after it, or under the last open row (an open last Task's last Subtask row).
    /// </summary>
    public void ShowDropGap(int gap)
    {
        var y = ReorderGeometry.LineOffset(OpenRowSpans(), ViewModel.RowGapOfTaskGap(gap));
        if (y is not { } top)
        {
            HideInsertionLine();
            return;
        }

        TaskInsertionLine.Margin = new Thickness(0, Math.Max(0, top - (TaskInsertionLine.Height / 2)), 0, 0);
        TaskInsertionLine.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The drag left the spot at <paramref name="position"/> (relative to the list). Moving between rows leaves a
    /// row too, so the line hides only once the position is outside the list.
    /// </summary>
    public void LeaveTaskDrag(Windows.Foundation.Point position)
    {
        if (ReorderGeometry.HasLeft(position.X, position.Y, OpenTaskList.ActualWidth, OpenTaskList.ActualHeight))
        {
            HideInsertionLine();
        }
    }

    private void HideInsertionLine() => TaskInsertionLine.Visibility = Visibility.Collapsed;

    FrameworkElement IReorderDragHost.Surface => OpenTaskList;

    Windows.UI.Color IReorderDragHost.PictureBacking => ((SolidColorBrush)DragPictureBacking.Background).Color;

    // Only an open Task row drags, on a List's page: never an Overdue, completed or Subtask row. Its picture is its
    // row and, while it is open, its Subtask rows.
    DragSubject? IReorderDragHost.SubjectAt(Windows.Foundation.Point at)
    {
        if (!ViewModel.IsReorderEnabled || RowAt(at) is not { } row
            || OpenTaskList.ContainerFromItem(row) is not FrameworkElement container)
        {
            return null;
        }

        var rows = new List<FrameworkElement> { container };
        var items = OpenTaskList.Items;
        for (var i = items.IndexOf(row) + 1;
             i < items.Count && items[i] is SubtaskListRowViewModel subtask && subtask.TaskRow == row;
             i++)
        {
            if (OpenTaskList.ContainerFromIndex(i) is FrameworkElement subtaskContainer)
            {
                rows.Add(subtaskContainer);
            }
        }

        return new DragSubject(row, rows);
    }

    void IReorderDragHost.ShowDragAt(Windows.Foundation.Point at)
    {
        if (ReorderGeometry.HasLeft(at.X, at.Y, OpenTaskList.ActualWidth, OpenTaskList.ActualHeight))
        {
            LeaveTaskDrag(at);
        }
        else
        {
            ShowDropGap(GapAt(at));
        }
    }

    // The drag ended: a scroll step it left in flight no longer counts.
    void IReorderDragHost.HideDragLine()
    {
        _edgeScrollTarget = null;
        HideInsertionLine();
    }

    // The gap under the pointer, measured against the list as it is at the release. Below the open rows (empty
    // space, the Completed heading, a completed row) is the last gap: the end.
    Task IReorderDragHost.DropAsync(object item, Windows.Foundation.Point at) =>
        DropIntoGapAsync((TaskRowViewModel)item, GapAt(at));

    bool IReorderDragHost.ScrollBy(double dy)
    {
        if (_listScroll is not { } scroll)
        {
            return false;
        }

        // A step before the last one has landed goes on from where that one is headed, so no step is lost.
        var from = _edgeScrollTarget ?? scroll.VerticalOffset;
        var target = Math.Clamp(from + dy, 0, scroll.ScrollableHeight);
        if (target == from || !scroll.ChangeView(null, target, null, disableAnimation: true))
        {
            return false;
        }

        _edgeScrollTarget = target;
        return true;
    }

    // The list's own scroll: a drag scrolls it near the list's edges, and the line follows the rows as it scrolls.
    private void OnTaskListLoaded(object sender, RoutedEventArgs e)
    {
        if (_listScroll is null
            && VisualTreeWalk.Descendants(OpenTaskList).OfType<ScrollViewer>().FirstOrDefault() is { } scroll)
        {
            _listScroll = scroll;
            scroll.ViewChanged += OnListViewChanged;
        }
    }

    private void OnListViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!e.IsIntermediate)
        {
            _edgeScrollTarget = null;
        }

        TaskDrag.ListScrolled();
    }

    // The mouse's left button drags, and touch and pen by press-and-hold; a press on a row's checkbox or chevron
    // does not.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "XAML event handler; the signature requires void.")]
    private async void OnDragPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(OpenTaskList);
        if (DragPointerOf(e.Pointer.PointerDeviceType) is not { } pointer
            || (pointer == DragPointer.Mouse ? !point.Properties.IsLeftButtonPressed : !point.IsInContact)
            || IsOnButton(e.OriginalSource as DependencyObject))
        {
            TaskDrag.Cancel();
            return;
        }

        await TaskDrag.PressAsync(point.Position, pointer);
    }

    private static DragPointer? DragPointerOf(Microsoft.UI.Input.PointerDeviceType device) => device switch
    {
        Microsoft.UI.Input.PointerDeviceType.Mouse => DragPointer.Mouse,
        Microsoft.UI.Input.PointerDeviceType.Touch => DragPointer.Touch,
        Microsoft.UI.Input.PointerDeviceType.Pen => DragPointer.Pen,
        _ => null,
    };

    private static bool IsOnButton(DependencyObject? element)
    {
        for (; element is not null and not ListViewItem; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    // The list captures the pointer once the drag starts, so the moves and the release keep coming outside it.
    private void OnDragPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var starting = !TaskDrag.IsDragging;
        if (!TaskDrag.Move(e.GetCurrentPoint(OpenTaskList).Position))
        {
            return;
        }

        if (starting)
        {
            OpenTaskList.CapturePointer(e.Pointer);
        }

        e.Handled = true;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "XAML event handler; the signature requires void.")]
    private async void OnDragPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!TaskDrag.TryRelease(e.GetCurrentPoint(OpenTaskList).Position, out var drop))
        {
            return;
        }

        e.Handled = true;
        OpenTaskList.ReleasePointerCapture(e.Pointer);
        await drop;
    }

    // The dragged Task left the open rows (deleted, completed, or the page reloaded with new rows): the drag ends
    // as a cancel. A change that keeps it there, another Task added say, leaves the drag on.
    private void CancelDragOfGoneTask()
    {
        if (TaskDrag.Item is TaskRowViewModel row && !ViewModel.Tasks.Contains(row))
        {
            TaskDrag.Cancel();
        }
    }

    // Losing the pointer (Windows took it, or the list's scrolling did) ends the press or the drag as a cancel.
    private void OnDragPointerCaptureLost(object sender, PointerRoutedEventArgs e) => TaskDrag.Cancel();
}
