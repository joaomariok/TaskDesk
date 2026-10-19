using System.Diagnostics.CodeAnalysis;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TaskDesk.Core.Tags;
using TaskDesk.Presentation;
using Windows.ApplicationModel.DataTransfer;
using VirtualKey = Windows.System.VirtualKey;

namespace TaskDesk.Views;

// The Lists and Tags under the Smart Views: Lists (drag to reorder) with "+ New List", then Tags. Rename, Colour
// and Delete live on the context menu of each List and Tag (right click, Shift+F10 or the Menu key); Delete on a
// focused List or Tag starts its deletion with the usual confirmation. In the rail the Lists show their colour squares
// and the Tags move behind the # button.
public sealed partial class NavigationPane
{
    private readonly Dialogs.DialogService _dialogs;
    private bool _compact;
    private bool _selectionSyncQueued;
    private ListItemViewModel? _menuList;
    private Tag? _menuTag;
    private ListItemViewModel? _dragged;

    /// <summary>The Lists section, or null when the pane shows no Lists.</summary>
    public ListsViewModel? Lists { get; }

    /// <summary>The colour of a List (#RRGGBB) as a brush; transparent for none.</summary>
    public static Brush ColourBrush(string? colorHex) =>
        ListColour.TryParse(colorHex, out var colour)
            ? new SolidColorBrush(colour)
            : new SolidColorBrush(Colors.Transparent);

    /// <summary>Shown only for a List with a colour.</summary>
    public static Visibility ShownWithColour(string? colorHex) =>
        ListColour.TryParse(colorHex, out _) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Shown only for a List without a colour (the hollow ring or square).</summary>
    public static Visibility ShownWithoutColour(string? colorHex) =>
        ListColour.TryParse(colorHex, out _) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Shown only while there is text, such as an error message.</summary>
    public static Visibility ShownWithText(string? text) =>
        string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Creates the List named in the new-List box (what Enter there does), then closes the box.</summary>
    public async Task CreateListAsync()
    {
        if (Lists is null)
        {
            return;
        }

        await Lists.CreateListCommand.ExecuteAsync(null);
        if (Lists.NewListName.Length == 0)
        {
            HideNewListBox();
            NewListButton.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>
    /// Drops <paramref name="dragged"/> into the gap <paramref name="gap"/> (0 is before the first List, the number
    /// of Lists after the last) through the Lists' reorder use case, and hides the insertion line. Its own gap,
    /// above or below it, moves nothing.
    /// </summary>
    public async Task DropListAsync(ListItemViewModel dragged, int gap)
    {
        HideInsertionLine();
        if (Lists is not null)
        {
            await Lists.MoveToGapAsync(dragged, gap);
        }
    }

    /// <summary>
    /// The gap a drop at <paramref name="position"/> (relative to the Lists list) lands in: over a row's top half
    /// the gap before it, over its bottom half the gap after it, and past the rows the last gap.
    /// </summary>
    public int GapAt(Windows.Foundation.Point position)
    {
        for (var i = 0; i < ListsList.Items.Count; i++)
        {
            if (RowBounds(i) is { } row && position.Y < row.Top + row.Height / 2)
            {
                return i;
            }
        }

        return ListsList.Items.Count;
    }

    /// <summary>
    /// The drag left the spot at <paramref name="position"/> (relative to the Lists list). Moving between rows
    /// leaves a row too, so the line hides only once the position is outside the Lists.
    /// </summary>
    public void LeaveListDrag(Windows.Foundation.Point position)
    {
        if (position.X < 0 || position.Y < 0 || position.X >= ListsList.ActualWidth || position.Y >= ListsList.ActualHeight)
        {
            HideInsertionLine();
        }
    }

    /// <summary>The List drag completed or was cancelled: the line hides.</summary>
    public void EndListDrag()
    {
        _dragged = null;
        HideInsertionLine();
    }

    // The top and height of row i in the Lists list, or null while it has no container.
    private (double Top, double Height)? RowBounds(int index)
    {
        if (ListsList.ContainerFromIndex(index) is not FrameworkElement container)
        {
            return null;
        }

        var top = container.TransformToVisual(ListsList).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
        return (top, container.ActualHeight);
    }

    /// <summary>
    /// Shows the insertion line on the gap <paramref name="gap"/> while a List is dragged: at the top of the row
    /// after it, or at the bottom of the last row.
    /// </summary>
    public void ShowListDropGap(int gap)
    {
        var count = ListsList.Items.Count;
        var y = gap < count ? RowBounds(gap)?.Top
            : count > 0 && RowBounds(count - 1) is { } last ? last.Top + last.Height
            : null;
        if (y is not { } top)
        {
            HideInsertionLine();
            return;
        }

        ListInsertionLine.Margin = new Thickness(_compact ? 6 : 12, Math.Max(0, top - ListInsertionLine.Height / 2), _compact ? 6 : 12, 0);
        ListInsertionLine.Visibility = Visibility.Visible;
    }

    private void HideInsertionLine() => ListInsertionLine.Visibility = Visibility.Collapsed;

    private void InitializeListsAndTags()
    {
        ListsList.ItemsSource = Lists?.Lists;
        if (Lists is not null)
        {
            // A reload of the Lists (after a change, or from the quick-add window) loses their selection and may
            // have changed the counts.
            Lists.Lists.CollectionChanged += (_, _) => QueueSelectionSync();
        }
        else
        {
            ListsHeader.Visibility = Visibility.Collapsed;
            NewListArea.Visibility = Visibility.Collapsed;
        }

        ViewModel.Tags.CollectionChanged += (_, _) =>
        {
            ShowTags();
            QueueSelectionSync();
        };
        ShowTags();
        // The rail's hamburger opens the full pane as an overlay, which shows the full Lists and Tags.
        Pane.PaneOpening += (_, _) => ShowCompact(false);
        Pane.PaneClosing += (_, _) => ShowCompact(IsRail);
    }

    // The List and Tag rows show in the pane's footer, whose row normally sits at the bottom of the pane. Giving
    // the Smart Views their own height and the footer the rest puts the Lists right under the Smart Views.
    private static void FitFooterUnderMenuItems(Grid itemsContainer)
    {
        if (itemsContainer.RowDefinitions.Count >= 3)
        {
            itemsContainer.RowDefinitions[0].Height = GridLength.Auto;
            itemsContainer.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
        }
    }

    // The rail (the closed compact pane) shows each List as a square of its colour, no "+ New List", and the Tags
    // behind the # button; the full pane, also as the rail's overlay, shows everything.
    private void ShowCompact(bool compact)
    {
        if (compact == _compact)
        {
            return;
        }

        _compact = compact;
        var full = compact || Lists is null ? Visibility.Collapsed : Visibility.Visible;
        ListsHeader.Visibility = full;
        NewListArea.Visibility = full;
        ShowTags();
        // The closed compact pane left-aligns its footer, which would shrink the rows to their markers' width.
        SidebarScroll.Width = compact ? Pane.CompactPaneLength : double.NaN;

        ListsList.ItemsSource = null;
        ListsList.ItemTemplate = (DataTemplate)Resources[compact ? "RailListRowTemplate" : "ListRowTemplate"];
        ListsList.ItemContainerStyle = (Style)Resources[compact ? "RailListItem" : "SidebarListItem"];
        ListsList.ItemsSource = Lists?.Lists;
        AutomationProperties.SetAutomationId(ListsList, compact ? "RailListsList" : "ListsList");
        SyncSelection();
    }

    private void ShowTags()
    {
        var any = ViewModel.Tags.Count > 0;
        TagsSection.Visibility = any && !_compact ? Visibility.Visible : Visibility.Collapsed;
        RailTagsButton.Visibility = any && _compact ? Visibility.Visible : Visibility.Collapsed;
    }

    private void QueueSelectionSync()
    {
        if (_selectionSyncQueued)
        {
            return;
        }

        _selectionSyncQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _selectionSyncQueued = false;
            SyncSelection();
        });
    }

    // Part of SyncSelection: a Tag's page selects its Tag, a List's page that List (the Default List until the
    // first load knows its id), and a Smart View neither.
    private void SyncListAndTagSelection()
    {
        var tag = ViewModel.TagFilter is { } filter ? ViewModel.Tags.FirstOrDefault(t => t.Id == filter.Id) : null;
        TagList.SelectedItem = tag;
        RailTagList.SelectedItem = tag;
        ListsList.SelectedItem = ViewModel.IsListPage ? Lists?.ListOrDefault(ViewModel.CurrentListId) : null;
    }

    // Selecting a List (by click or by keyboard) opens its page.
    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && ListsList.SelectedItem is ListItemViewModel item && ViewModel.CurrentListId != item.Id)
        {
            await ViewModel.ShowListAsync(item.Id);
        }
    }

    // A click opens the List's page even when it is selected already, which ends a search there.
    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnListItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ListItemViewModel item)
        {
            await ViewModel.ShowListAsync(item.Id);
        }
    }

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnTagSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && sender is ListView { SelectedItem: Tag tag } && ViewModel.TagFilter?.Id != tag.Id)
        {
            TagsFlyout.Hide();
            await ViewModel.FilterByTagAsync(tag);
        }
    }

    // Each row gets its context menu (on the row, so Shift+F10 and the Menu key on a focused row open it) and its
    // accessible name, which the UI tests find it by.
    private void OnListContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is ListItemViewModel item)
        {
            args.ItemContainer.ContextFlyout = (FlyoutBase)Resources["ListMenu"];
            AutomationProperties.SetName(args.ItemContainer, item.Name);
        }
    }

    private void OnTagContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is Tag tag)
        {
            args.ItemContainer.ContextFlyout = (FlyoutBase)Resources["TagMenu"];
            AutomationProperties.SetName(args.ItemContainer, tag.Name);
        }
    }

    // The menu is shared; the row it opens on is the List it acts on. The Default List cannot be deleted.
    private void OnListMenuOpening(object? sender, object e)
    {
        var menu = (MenuFlyout)sender!;
        _menuList = (menu.Target as SelectorItem)?.Content as ListItemViewModel;
        menu.Items[^1].IsEnabled = _menuList is { IsDefault: false };
    }

    private void OnTagMenuOpening(object? sender, object e) =>
        _menuTag = (((MenuFlyout)sender!).Target as SelectorItem)?.Content as Tag;

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (Lists is not null && _menuList is { } item)
        {
            await Lists.RenameCommand.ExecuteAsync(item);
        }
    }

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnColourClick(object sender, RoutedEventArgs e)
    {
        // None's empty Tag arrives as null, not "", so read it as a nullable string.
        if (Lists is not null && _menuList is { } item && sender is MenuFlyoutItem menuItem)
        {
            await Lists.SetColorAsync(item, menuItem.Tag as string);
        }
    }

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnDeleteListClick(object sender, RoutedEventArgs e)
    {
        if (Lists is not null && _menuList is { } item)
        {
            await Lists.DeleteCommand.ExecuteAsync(item);
        }
    }

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnDeleteTagClick(object sender, RoutedEventArgs e)
    {
        if (_menuTag is { } tag)
        {
            await DeleteTagAsync(tag);
        }
    }

    // Deleting a Tag removes it from every Task and cannot be undone, so it asks first.
    private async Task DeleteTagAsync(Tag tag)
    {
        if (await _dialogs.ConfirmAsync(
                "Delete tag",
                $"Delete the tag #{tag.Name}? It is removed from all tasks. This cannot be undone.",
                "Delete",
                destructive: true))
        {
            await ViewModel.DeleteTagAsync(tag);
        }
    }

    // Delete on a focused List or Tag starts its deletion with the usual confirmation.
    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Delete && Lists is not null && e.OriginalSource is SelectorItem { Content: ListItemViewModel item })
        {
            e.Handled = true;
            await Lists.DeleteCommand.ExecuteAsync(item);
        }
    }

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnTagKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Delete && e.OriginalSource is SelectorItem { Content: Tag tag })
        {
            e.Handled = true;
            await DeleteTagAsync(tag);
        }
    }

    // ---- New List ----

    private void OnNewListClick(object sender, RoutedEventArgs e)
    {
        NewListButton.Visibility = Visibility.Collapsed;
        NewListBox.Visibility = Visibility.Visible;
        NewListBox.Focus(FocusState.Programmatic);
    }

    private void HideNewListBox()
    {
        NewListBox.Visibility = Visibility.Collapsed;
        NewListButton.Visibility = Visibility.Visible;
    }

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnNewListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && Lists is not null)
        {
            e.Handled = true;
            Lists.NewListName = "";
            HideNewListBox();
            NewListButton.Focus(FocusState.Keyboard);
        }
        else if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await CreateListAsync();
        }
    }

    private void OnNewListLostFocus(object sender, RoutedEventArgs e)
    {
        if (Lists is null || Lists.NewListName.Length == 0)
        {
            HideNewListBox();
        }
    }

    // ---- Drag to reorder Lists: an insertion line shows the gap under the pointer, and the drop lands there ----

    private void OnListDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        _dragged = e.Items.FirstOrDefault() as ListItemViewModel;
        if (_dragged is null || Lists is null)
        {
            e.Cancel = true;
            return;
        }

        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = _dragged is not null ? DataPackageOperation.Move : DataPackageOperation.None;
        e.Handled = true;
        if (_dragged is not null)
        {
            ShowListDropGap(GapAt(e.GetPosition(ListsList)));
        }
        else
        {
            HideInsertionLine();
        }
    }

    // Moving between rows raises a row's DragLeave too, which bubbles here.
    private void OnListDragLeave(object sender, DragEventArgs e) => LeaveListDrag(e.GetPosition(ListsList));

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnListDrop(object sender, DragEventArgs e)
    {
        var dragged = _dragged;
        _dragged = null;
        if (dragged is not null)
        {
            await DropListAsync(dragged, GapAt(e.GetPosition(ListsList)));
        }
        else
        {
            HideInsertionLine();
        }
    }

    private void OnListDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => EndListDrag();
}
