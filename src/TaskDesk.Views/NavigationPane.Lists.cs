using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
// focused List or Tag starts its deletion with the usual confirmation. In the rail the Lists show their initials
// and the Tags move behind the # button.
public sealed partial class NavigationPane
{
    private readonly Dialogs.DialogService? _dialogs;
    private bool _compact;
    private bool _selectionSyncQueued;
    private ListItemViewModel? _menuList;
    private Tag? _menuTag;
    private ListItemViewModel? _dragged;
    private int _dragFrom = -1;

    /// <summary>The Lists section, or null when the pane shows no Lists.</summary>
    public ListsViewModel? Lists { get; }

    /// <summary>The colour of a List (#RRGGBB) as a brush; transparent for none.</summary>
    public static Brush ColourBrush(string? colorHex) =>
        TryParseColour(colorHex, out var colour)
            ? new SolidColorBrush(colour)
            : new SolidColorBrush(Colors.Transparent);

    /// <summary>Shown only for a List with a colour.</summary>
    public static Visibility ShownWithColour(string? colorHex) =>
        TryParseColour(colorHex, out _) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Shown only for a List without a colour (the hollow ring).</summary>
    public static Visibility ShownWithoutColour(string? colorHex) =>
        TryParseColour(colorHex, out _) ? Visibility.Collapsed : Visibility.Visible;

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
    /// Saves the order a reorder drag left: <paramref name="item"/> was at <paramref name="fromIndex"/> and the
    /// list has moved it to where it is now. The move goes through the Lists' reorder use case.
    /// </summary>
    public async Task SaveListMoveAsync(ListItemViewModel item, int fromIndex)
    {
        if (Lists is null)
        {
            return;
        }

        var to = Lists.Lists.IndexOf(item);
        if (fromIndex < 0 || to < 0 || to == fromIndex)
        {
            return;
        }

        // MoveAsync takes the List and the one now at its new place, counted from the order before the move.
        Lists.Lists.Move(to, fromIndex);
        await Lists.MoveAsync(item, Lists.Lists[to]);
    }

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

    // The rail (the closed compact pane) shows each List as its initial in a ring, no "+ New List", and the Tags
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

    // Part of SyncSelection: a Tag's page selects its Tag, the Default List's page a List (the Default List
    // unless another is picked), and a Smart View neither.
    private void SyncListAndTagSelection()
    {
        var tag = ViewModel.TagFilter is { } filter ? ViewModel.Tags.FirstOrDefault(t => t.Id == filter.Id) : null;
        TagList.SelectedItem = tag;
        RailTagList.SelectedItem = tag;
        if (ViewModel.CurrentView is not null || ViewModel.TagFilter is not null)
        {
            ListsList.SelectedItem = null;
        }
        else if (ListsList.SelectedItem is null && Lists is not null)
        {
            ListsList.SelectedItem = Lists.Lists.FirstOrDefault(l => l.IsDefault);
        }
    }

    // Selecting the Default List shows it again after a Smart View or a Tag; other Lists keep their behaviour.
    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && ListsList.SelectedItem is ListItemViewModel { IsDefault: true }
            && (ViewModel.CurrentView is not null || ViewModel.TagFilter is not null))
        {
            await ViewModel.FilterByTagAsync(null);
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
        if (Lists is not null && _menuList is { } item && sender is MenuFlyoutItem { Tag: string hex })
        {
            await Lists.SetColorAsync(item, hex);
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
        if (_dialogs is not null && await _dialogs.ConfirmAsync(
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

    // ---- Drag to reorder Lists ----

    private void OnListDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        _dragged = e.Items.FirstOrDefault() as ListItemViewModel;
        _dragFrom = _dragged is null || Lists is null ? -1 : Lists.Lists.IndexOf(_dragged);
    }

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnListDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var item = _dragged;
        _dragged = null;
        if (item is not null && args.DropResult == DataPackageOperation.Move)
        {
            await SaveListMoveAsync(item, _dragFrom);
        }
    }

    private static bool TryParseColour(string? hex, out Windows.UI.Color colour)
    {
        colour = default;
        if (hex is not { Length: 7 } || hex[0] != '#'
            || !uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        colour = ColorHelper.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
}
