using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TaskDesk.App.Dialogs;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;

namespace TaskDesk.App.Views;

/// <summary>
/// The sidebar: Smart Views with counts, Lists (drag to reorder) and Tags. Rename, Colour and Delete
/// live on the context menu of each List and Tag (right-click, Menu key or Shift+F10); Delete on a
/// focused List or Tag starts its deletion.
/// </summary>
public partial class NavigationPane : UserControl
{
    private Point _listDragStart;
    private bool _syncing;
    private bool _countRefreshQueued;

    public NavigationPane()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel { get; set; } = null!;
    private ListsViewModel Lists { get; set; } = null!;

    /// <summary>Connects the pane to the view models; called once by <see cref="MainWindow"/>.</summary>
    public void Initialize(MainViewModel viewModel, ListsViewModel lists)
    {
        ViewModel = viewModel;
        Lists = lists;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.CurrentView) or nameof(MainViewModel.TagFilter))
            {
                SyncSelection();
            }
        };
        viewModel.OpenTaskCountsChanged += (_, _) => QueueCountRefresh();

        // Every reload of the Lists (including from the quick-add window) clears their selection and may
        // have changed the counts.
        lists.Lists.CollectionChanged += (_, _) => QueueCountRefresh();
        Loaded += (_, _) => QueueCountRefresh();
    }

    // Coalesces the bursts of change events of one reload into a single recount and re-sync.
    private void QueueCountRefresh()
    {
        if (_countRefreshQueued)
        {
            return;
        }

        _countRefreshQueued = true;
        _ = Dispatcher.InvokeAsync(RefreshAsync, DispatcherPriority.Background);
    }

    private async Task RefreshAsync()
    {
        _countRefreshQueued = false;
        SyncSelection();
        try
        {
            await ViewModel.RefreshSmartViewCountsAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The counts stay as they were; the next change refreshes them again.
        }
    }

    // Shows which view is current: one selected item across the three sections.
    private void SyncSelection()
    {
        if (ViewModel is null)
        {
            return;
        }

        _syncing = true;
        try
        {
            SmartViewList.SelectedItem = ViewModel.CurrentView is { } view
                ? SmartViewList.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is SmartView v && v == view)
                : null;
            TagList.SelectedItem = ViewModel.TagFilter;
            if (ViewModel.CurrentView is not null || ViewModel.TagFilter is not null)
            {
                ListsBox.SelectedItem = null;
            }
            else if (ListsBox.SelectedItem is null)
            {
                // Neither a Smart View nor a Tag: the Default List is showing.
                ListsBox.SelectedItem = Lists.Lists.FirstOrDefault(l => l.IsDefault);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnSmartViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && SmartViewList.SelectedItem is ListBoxItem { Tag: SmartView view })
        {
            await ViewModel.ShowSmartViewAsync(view);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnTagSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && TagList.SelectedItem is Tag tag)
        {
            await ViewModel.FilterByTagAsync(tag);
        }
    }

    // Selecting the Default List shows it again after a Smart View or a Tag; other Lists keep their behaviour.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && ListsBox.SelectedItem is ListItemViewModel { IsDefault: true }
            && (ViewModel.CurrentView is not null || ViewModel.TagFilter is not null))
        {
            await ViewModel.FilterByTagAsync(null);
        }
    }

    // ---- New List ----

    private void OnNewListClick(object sender, RoutedEventArgs e)
    {
        NewListButton.Visibility = Visibility.Collapsed;
        NewListBox.Visibility = Visibility.Visible;
        NewListBox.Focus();
    }

    private void HideNewListBox()
    {
        NewListBox.Visibility = Visibility.Collapsed;
        NewListButton.Visibility = Visibility.Visible;
    }

    private void OnNewListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Lists.NewListName = "";
            HideNewListBox();
            NewListButton.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Return)
        {
            // The Enter binding creates the List; close the box once it was created (the name is cleared).
            _ = Dispatcher.InvokeAsync(
                () =>
                {
                    if (Lists.NewListName.Length == 0)
                    {
                        HideNewListBox();
                        NewListButton.Focus();
                    }
                },
                DispatcherPriority.Background);
        }
    }

    private void OnNewListLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Lists.NewListName.Length == 0)
        {
            HideNewListBox();
        }
    }

    // ---- Context menus and Delete ----

    // A context menu belongs to a List or Tag row; its placement target carries the row's view model.
    // Items of a submenu (Colour) reach the menu through their parent items.
    private static object? ItemOf(object sender)
    {
        DependencyObject? current = sender as MenuItem;
        while (current is MenuItem item)
        {
            current = ItemsControl.ItemsControlFromItemContainer(item);
        }

        return current is ContextMenu { PlacementTarget: FrameworkElement target } ? target.DataContext : null;
    }

    // The Default List cannot be deleted, so its Delete entry is disabled.
    private void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget: FrameworkElement { DataContext: ListItemViewModel item } } menu)
        {
            foreach (var entry in menu.Items.OfType<MenuItem>().Where(m => m.Tag is "delete"))
            {
                entry.IsEnabled = !item.IsDefault;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is ListItemViewModel item)
        {
            await Lists.RenameCommand.ExecuteAsync(item);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is ListItemViewModel item)
        {
            await Lists.DeleteCommand.ExecuteAsync(item);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnColorClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is ListItemViewModel item && sender is MenuItem { Tag: string hex })
        {
            await Lists.SetColorAsync(item, hex);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnDeleteTagClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is Tag tag)
        {
            await DeleteTagAsync(tag);
        }
    }

    private async Task DeleteTagAsync(Tag tag)
    {
        // The Fluent dialog is centred on the main window.
        if (FluentDialogService.Confirm(
            "Delete tag",
            $"Delete the tag #{tag.Name}? It is removed from all tasks. This cannot be undone.",
            "Delete",
            Window.GetWindow(this)))
        {
            await ViewModel.DeleteTagAsync(tag);
        }
    }

    // Delete on a focused List or Tag starts its deletion with the usual confirmation.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Keyboard.FocusedElement is ListBoxItem { DataContext: ListItemViewModel item })
        {
            e.Handled = true;
            await Lists.DeleteCommand.ExecuteAsync(item);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnTagKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Keyboard.FocusedElement is ListBoxItem { DataContext: Tag tag })
        {
            e.Handled = true;
            await DeleteTagAsync(tag);
        }
    }

    // ---- Drag to reorder Lists ----

    private void OnListMouseDown(object sender, MouseButtonEventArgs e) => _listDragStart = e.GetPosition(null);

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not ListBox box)
        {
            return;
        }

        var delta = e.GetPosition(null) - _listDragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (box.SelectedItem is ListItemViewModel item)
        {
            DragDrop.DoDragDrop(box, item, DragDropEffects.Move);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnListDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ListItemViewModel)) is ListItemViewModel dragged
            && (e.OriginalSource as FrameworkElement)?.DataContext is ListItemViewModel target)
        {
            await Lists.MoveAsync(dragged, target);
        }
    }
}
