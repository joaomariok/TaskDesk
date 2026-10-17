using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;

namespace TaskDesk.Views;

/// <summary>
/// The Task list area: the Overdue, open and "Completed (N)" Task lists, their keyboard shortcuts and the Undo
/// bar. Only one of the three lists has a selection at a time; it is the view model's selected Task.
/// </summary>
public sealed partial class TaskListView : UserControl
{
    // Each Task list with the view model rows it shows.
    private readonly (ListView List, ObservableCollection<TaskRowViewModel> Rows)[] _lists;
    private bool _syncing;

    public TaskListView(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        _lists =
        [
            (OverdueTaskList, viewModel.OverdueTasks), (OpenTaskList, viewModel.Tasks),
            (CompletedTaskList, viewModel.CompletedTasks),
        ];

        // A completed Task lingers struck through for a moment, unless Windows asks for less animation.
        viewModel.CompletionMoveDelay = () => CompletionHold.For(AnimationsEnabled());
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        foreach (var (list, rows) in _lists)
        {
            // With no rows (the Completed view, an empty List) a list takes no focus, so Tab skips it.
            list.IsTabStop = rows.Count > 0;
            rows.CollectionChanged += (_, _) => list.IsTabStop = rows.Count > 0;
            list.Tapped += OnTaskListTapped;
        }
    }

    public MainViewModel ViewModel { get; }

    /// <summary>Windows' "Animation effects" setting; replaceable so tests need not depend on the machine.</summary>
    public Func<bool> AnimationsEnabled { get; set; } = () => new UISettings().AnimationsEnabled;

    /// <summary>
    /// Runs the Task list shortcut for a key: Space completes and Delete deletes the selected Task, Enter opens it
    /// in the detail pane, Ctrl+Z undoes the last delete or completion. Returns false when the key is not a
    /// shortcut or there is nothing to do.
    /// </summary>
    public bool TryShortcut(VirtualKey key, VirtualKeyModifiers modifiers)
    {
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

    /// <summary>Selects a Task's row and gives it keyboard focus (after Esc closed the detail pane).</summary>
    public void FocusRow(TaskRowViewModel row)
    {
        var list = _lists.FirstOrDefault(l => l.Rows.Contains(row)).List;
        if (list is null)
        {
            return;
        }

        list.SelectedItem = row;
        list.ScrollIntoView(row);
        list.UpdateLayout();
        (list.ContainerFromItem(row) as ListViewItem)?.Focus(FocusState.Keyboard);
    }

    // A click on a row opens the detail pane on it (arrow keys only move the selection); a click on the row's
    // checkbox only completes the Task.
    private void OnTaskListTapped(object sender, TappedRoutedEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element is not null and not ListView;
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase)
            {
                return;
            }

            if (element is ListViewItem { Content: TaskRowViewModel row })
            {
                ViewModel.OpenDetailPane(row);
                return;
            }
        }
    }

    // Space and Delete act on a focused row. A row's own checkbox handles Space itself.
    private void OnTaskListPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is ListViewItem && TryShortcut(e.Key, CurrentModifiers()))
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

    private static VirtualKeyModifiers CurrentModifiers()
    {
        var modifiers = VirtualKeyModifiers.None;
        foreach (var (key, flag) in new[]
                 {
                     (VirtualKey.Control, VirtualKeyModifiers.Control), (VirtualKey.Shift, VirtualKeyModifiers.Shift),
                     (VirtualKey.Menu, VirtualKeyModifiers.Menu),
                 })
        {
            if (InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down))
            {
                modifiers |= flag;
            }
        }

        return modifiers;
    }

    // UI Automation names a row by its Task title (the UI tests find rows by name); the row's checkbox runs
    // the view model's toggle.
    private void OnRowContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not TaskRowViewModel row)
        {
            return;
        }

        AutomationProperties.SetName(args.ItemContainer, row.Title);
        if (args.ItemContainer.ContentTemplateRoot is TaskRow view)
        {
            view.ToggleCompleteCommand = ViewModel.ToggleCompleteCommand;
        }
    }

    private void OnTaskSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Removals happen when a row moves between groups; the selection then follows the row.
        if (_syncing || e.AddedItems.Count == 0 || e.AddedItems[0] is not TaskRowViewModel row)
        {
            return;
        }

        _syncing = true;
        try
        {
            foreach (var (other, _) in _lists)
            {
                if (!ReferenceEquals(sender, other))
                {
                    other.SelectedItem = null;
                }
            }
        }
        finally
        {
            _syncing = false;
        }

        ViewModel.SelectedTask = row;
    }

    // Shows the view model's selection (for example after a reload) in whichever list holds the row.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SelectedTask))
        {
            return;
        }

        var row = ViewModel.SelectedTask;
        _syncing = true;
        try
        {
            foreach (var (list, rows) in _lists)
            {
                list.SelectedItem = row is not null && rows.Contains(row) ? row : null;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    // ---- Drag to reorder (the open list, in a List view only) ----

    private TaskRowViewModel? _dragged;

    /// <summary>
    /// The open Task row under <paramref name="position"/> (relative to the open list), or null over empty space.
    /// </summary>
    public TaskRowViewModel? RowAt(Windows.Foundation.Point position)
    {
        for (var i = 0; i < ViewModel.Tasks.Count; i++)
        {
            if (OpenTaskList.ContainerFromIndex(i) is not FrameworkElement container)
            {
                continue;
            }

            var top = container.TransformToVisual(OpenTaskList).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            if (position.Y >= top && position.Y < top + container.ActualHeight)
            {
                return ViewModel.Tasks[i];
            }
        }

        return null;
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

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (!ViewModel.IsReorderEnabled || e.Items.Count != 1 || e.Items[0] is not TaskRowViewModel row)
        {
            e.Cancel = true;
            return;
        }

        _dragged = row;
        e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
    }

    private void OnTaskDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = _dragged is not null && ViewModel.IsReorderEnabled
            ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move
            : Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
        e.Handled = true;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "XAML event handler; the signature requires void.")]
    private async void OnTaskDrop(object sender, DragEventArgs e)
    {
        var dragged = _dragged;
        _dragged = null;
        if (dragged is null)
        {
            return;
        }

        await DropAsync(dragged, RowAt(e.GetPosition(OpenTaskList)));
    }
}
