using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
        }
    }

    public MainViewModel ViewModel { get; }

    /// <summary>Windows' "Animation effects" setting; replaceable so tests need not depend on the machine.</summary>
    public Func<bool> AnimationsEnabled { get; set; } = () => new UISettings().AnimationsEnabled;

    /// <summary>
    /// Runs the Task list shortcut for a key: Space completes and Delete deletes the selected Task, Ctrl+Z undoes
    /// the last delete or completion. Returns false when the key is not a shortcut or there is nothing to do.
    /// </summary>
    public bool TryShortcut(VirtualKey key, VirtualKeyModifiers modifiers)
    {
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
}
