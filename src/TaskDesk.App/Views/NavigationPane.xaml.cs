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
        ListsPanel.DataContext = lists;
        RailListsBox.ItemsSource = lists.Lists;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.CurrentView) or nameof(MainViewModel.TagFilter))
            {
                SyncSelection();
            }
        };
        // Every reload of the Lists (including from the quick-add window) clears their selection and may
        // have changed the counts. OpenTaskCountsChanged is not handled here: the main window answers it
        // by reloading the Lists, which lands here, so handling both would recount twice per change.
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
            foreach (var box in new[] { SmartViewList, RailSmartViewList })
            {
                box.SelectedItem = ViewModel.CurrentView is { } view
                    ? box.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is SmartView v && v == view)
                    : null;
            }

            TagList.SelectedItem = ViewModel.TagFilter;
            RailTagList.SelectedItem = ViewModel.TagFilter;
            if (ViewModel.CurrentView is not null || ViewModel.TagFilter is not null)
            {
                ListsBox.SelectedItem = null;
            }
            else if (ListsBox.SelectedItem is null)
            {
                // Neither a Smart View nor a Tag: the Default List is showing.
                ListsBox.SelectedItem = Lists.Lists.FirstOrDefault(l => l.IsDefault);
            }

            RailListsBox.SelectedItem = ListsBox.SelectedItem;
        }
        finally
        {
            _syncing = false;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnSmartViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && sender is ListBox { SelectedItem: ListBoxItem { Tag: SmartView view } })
        {
            ClosePopups();
            await ViewModel.ShowSmartViewAsync(view);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnTagSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && sender is ListBox { SelectedItem: Tag tag })
        {
            ClosePopups();
            await ViewModel.FilterByTagAsync(tag);
        }
    }

    // Selecting the Default List shows it again after a Smart View or a Tag; other Lists keep their behaviour.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || sender is not ListBox { SelectedItem: ListItemViewModel picked } box)
        {
            return;
        }

        // The full list and the rail's list show the same selection.
        _syncing = true;
        try
        {
            (box == ListsBox ? RailListsBox : ListsBox).SelectedItem = picked;
        }
        finally
        {
            _syncing = false;
        }

        ClosePopups();
        if (picked.IsDefault
            && (ViewModel.CurrentView is not null || ViewModel.TagFilter is not null))
        {
            await ViewModel.FilterByTagAsync(null);
        }
    }

    // ---- Icon rail (below 900 px) ----

    /// <summary>True while the sidebar shows as the icon rail.</summary>
    public bool IsRail { get; private set; }

    /// <summary>
    /// Switches between the full sidebar and the icon rail. In the rail, the full sidebar moves into the
    /// overlay (a pop-up opened by the hamburger button); widening the window moves it back.
    /// </summary>
    public void SetRail(bool rail)
    {
        if (rail == IsRail)
        {
            return;
        }

        IsRail = rail;
        ClosePopups();
        if (rail)
        {
            FullHost.Children.Remove(FullSidebar);
            OverlayHost.Child = FullSidebar;
        }
        else
        {
            OverlayHost.Child = null;
            FullHost.Children.Add(FullSidebar);
        }

        FullHost.Visibility = rail ? Visibility.Collapsed : Visibility.Visible;
        RailPanel.Visibility = rail ? Visibility.Visible : Visibility.Collapsed;
    }

    // Closes the overlay and the Tags flyout; focus goes back to the hamburger when one was open.
    private void ClosePopups()
    {
        var wasOpen = Overlay.IsOpen || TagsFlyout.IsOpen;
        Overlay.IsOpen = false;
        TagsFlyout.IsOpen = false;
        if (wasOpen && IsRail)
        {
            ExpandButton.Focus();
        }
    }

    private void OnExpandClick(object sender, RoutedEventArgs e) => Overlay.IsOpen = true;

    private void OnOverlayOpened(object? sender, EventArgs e)
    {
        OverlayHost.Height = Math.Max(ActualHeight, 120);
        _ = Dispatcher.InvokeAsync(() => FocusFirst(SmartViewList), DispatcherPriority.Background);
    }

    private void OnOverlayClosed(object? sender, EventArgs e) => OverlayHost.ClearValue(HeightProperty);

    private void OnOverlayKeyDown(object sender, KeyEventArgs e)
    {
        // Esc in the new-List box only cancels the new List.
        if (e.Key == Key.Escape && !ReferenceEquals(Keyboard.FocusedElement, NewListBox))
        {
            ClosePopups();
            e.Handled = true;
        }
    }

    private void OnRailTagsClick(object sender, RoutedEventArgs e) => TagsFlyout.IsOpen = true;

    private void OnTagsFlyoutOpened(object? sender, EventArgs e) =>
        _ = Dispatcher.InvokeAsync(() => FocusFirst(RailTagList), DispatcherPriority.Background);

    private void OnTagsFlyoutKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            TagsFlyout.IsOpen = false;
            RailTagsButton.Focus();
            e.Handled = true;
        }
    }

    // Focuses the selected row of a list, or its first row.
    private static void FocusFirst(ListBox box)
    {
        var item = box.SelectedItem ?? (box.Items.Count > 0 ? box.Items[0] : null);
        if (item is not null && box.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
        {
            container.Focus();
        }
        else
        {
            box.Focus();
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
            destructive: true,
            owner: Window.GetWindow(this)))
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
