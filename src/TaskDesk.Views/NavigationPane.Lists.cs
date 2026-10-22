using System.Diagnostics.CodeAnalysis;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tags;
using TaskDesk.Presentation;
using VirtualKey = Windows.System.VirtualKey;

namespace TaskDesk.Views;

// The Lists and Tags under the Smart Views: Lists (drag to reorder) with "+ New List", then Tags. Rename, Colour
// and Delete live on the context menu of each List and Tag (right click, Shift+F10 or the Menu key); Delete on a
// focused List or Tag starts its deletion with the usual confirmation. In the rail the Lists show their colour squares
// and the Tags move behind the # button.
public sealed partial class NavigationPane : IReorderDragHost
{
    private readonly Dialogs.DialogService _dialogs;
    private readonly DragScroller _dragScroll;
    private bool _compact;
    private bool _selectionSyncQueued;
    private ListItemViewModel? _menuList;
    private Tag? _menuTag;
    private bool _dragCheckQueued;
    private ListViewItem? _menulessRow;

    // True from a key pressed on the Lists to the next press: a click or selection then comes from the keyboard,
    // never from the release of a drag.
    private bool _keyInput;

    /// <summary>The drag that reorders the Lists (ADR 0007), fed by the Lists' pointer events.</summary>
    public ReorderDrag ListDrag { get; }

    /// <summary>The Lists section, or null when the pane shows no Lists.</summary>
    public SidebarViewModel? Lists { get; }

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
    public int GapAt(Windows.Foundation.Point position) =>
        ReorderGeometry.GapAt(RowMeasure.Spans(ListsList), position.Y);

    /// <summary>
    /// The drag left the spot at <paramref name="position"/> (relative to the Lists list). Moving between rows
    /// leaves a row too, so the line hides only once the position is outside the Lists.
    /// </summary>
    public void LeaveListDrag(Windows.Foundation.Point position)
    {
        if (ReorderGeometry.HasLeft(position.X, position.Y, ListsList.ActualWidth, ListsList.ActualHeight))
        {
            HideInsertionLine();
        }
    }

    /// <summary>The List drag completed or was cancelled: the line hides.</summary>
    public void EndListDrag() => HideInsertionLine();

    /// <summary>
    /// Runs the Lists' shortcut for a key: while a List is dragged, Esc cancels the drag. Returns false when the key
    /// is not a shortcut or there is nothing to do.
    /// </summary>
    public bool TryShortcut(VirtualKey key, Windows.System.VirtualKeyModifiers modifiers)
    {
        if ((key, modifiers) == (VirtualKey.Escape, Windows.System.VirtualKeyModifiers.None) && ListDrag.IsDragging)
        {
            ListDrag.Cancel();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Shows the insertion line on the gap <paramref name="gap"/> while a List is dragged: at the top of the row
    /// after it, or at the bottom of the last row.
    /// </summary>
    public void ShowListDropGap(int gap)
    {
        var y = ReorderGeometry.LineOffset(RowMeasure.Spans(ListsList), gap);
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
            Lists.Lists.CollectionChanged += (_, _) =>
            {
                QueueSelectionSync();
                QueueCancelDragOfGoneList();
            };
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
        ListDrag.ListenToPointer();
        ListDrag.LiftedAfterHold += OnListLiftedAfterHold;
        _dragScroll.Attach(SidebarScroll);
        ListDrag.Pressed += OnListPressed;
        ListDrag.Ended += (_, _) => RestoreRowMenu();
        ListsList.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => _keyInput = false), true);
        // A drag never carries over to another page.
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Page))
            {
                ListDrag.Cancel();
            }
        };
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
        ListDrag.Cancel(); // the rows it presses are replaced
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

    // Selecting a List (by keyboard, or a click on another List) opens its page. Opening a List ends the search, so
    // the List the sidebar already selects (as SyncSelection sets it) opens nothing here.
    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsReleaseOfDrag)
        {
            QueueSelectionSync(); // a drag never selects: the Lists go back to the page's List
            return;
        }

        if (ListsList.SelectedItem is ListItemViewModel item && item.Id != Sidebar.SelectedList?.Id)
        {
            await Sidebar.OpenAsync(new SidebarEntry.ListEntry(item.Id));
        }
    }

    // A click opens the List's page even when it is selected already, which ends a search there.
    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnListItemClick(object sender, ItemClickEventArgs e)
    {
        if (!IsReleaseOfDrag && e.ClickedItem is ListItemViewModel item)
        {
            await Sidebar.OpenAsync(new SidebarEntry.ListEntry(item.Id));
        }
    }

    // Selecting a Tag opens its page; the Tag already selected opens nothing and keeps the rail's flyout open.
    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnTagSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: Tag tag } && tag.Id != Sidebar.SelectedTag?.Id)
        {
            TagsFlyout.Hide();
            await Sidebar.OpenAsync(new SidebarEntry.TagEntry(tag));
        }
    }

    // Each row gets its context menu (on the row, so Shift+F10 and the Menu key on a focused row open it; a touch
    // hold does not, OnListPointerPressed) and its accessible name, which the UI tests find it by.
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

    // ---- Drag to reorder Lists (ADR 0007): an insertion line shows the gap under the pointer, and the drop lands there ----

    // A click or selection that comes from the release of a drag, rather than from a click or a key.
    private bool IsReleaseOfDrag => ListDrag.LastPressDragged && !_keyInput;

    FrameworkElement IReorderDragHost.Surface => ListsList;

    // The Lists scroll with "+ New List" and the Tags in the sidebar's scroll area: its edges scroll them.
    FrameworkElement IReorderDragHost.Viewport => SidebarScroll;

    Windows.UI.Color IReorderDragHost.PictureBacking => ((SolidColorBrush)ListDragPictureBacking.Background).Color;

    // Every List drags, in the full pane and in the rail; its picture is its row.
    DragSubject? IReorderDragHost.SubjectAt(Windows.Foundation.Point at)
    {
        if (Lists is null || at.X < 0 || at.X >= ListsList.ActualWidth)
        {
            return null;
        }

        var spans = RowMeasure.Spans(ListsList);
        for (var i = 0; i < spans.Length; i++)
        {
            if (spans[i] is { } span && at.Y >= span.Top && at.Y < span.Top + span.Height
                && ListsList.Items[i] is ListItemViewModel item
                && ListsList.ContainerFromIndex(i) is FrameworkElement container)
            {
                return new DragSubject(item, [container]);
            }
        }

        return null;
    }

    // Over Lists scrolled out of the sidebar's sight there is no gap to show.
    void IReorderDragHost.ShowDragAt(Windows.Foundation.Point at)
    {
        if (ReorderGeometry.HasLeft(at.X, at.Y, ListsList.ActualWidth, ListsList.ActualHeight))
        {
            LeaveListDrag(at);
        }
        else if (!ListDrag.IsOverList(at))
        {
            HideInsertionLine();
        }
        else
        {
            ShowListDropGap(GapAt(at));
        }
    }

    // The drag ended: a scroll step it left in flight no longer counts.
    void IReorderDragHost.HideDragLine()
    {
        _dragScroll.Stop();
        EndListDrag();
    }

    // The gap under the pointer, measured against the Lists as they are at the release. A reload since the press
    // replaced the rows, so the dragged List is found again by its id.
    Task IReorderDragHost.DropAsync(object item, Windows.Foundation.Point at)
    {
        var id = ((ListItemViewModel)item).Id;
        if (Lists?.Lists.FirstOrDefault(l => l.Id == id) is not { } dragged)
        {
            HideInsertionLine();
            return Task.CompletedTask;
        }

        return DropListAsync(dragged, GapAt(at));
    }

    bool IReorderDragHost.ScrollBy(double dy) => _dragScroll.ScrollBy(dy);

    // A touch or pen hold on a List is the start of its drag, so the hold opens no context menu: the menu's
    // light-dismiss layer would take the pointer and cancel the drag. The row WinUI would open it on (at the hold,
    // before any handler sees the request) has no menu until the press ends, however it ends (ListDrag.Ended): a
    // release anywhere, a swipe, a lost capture or the window losing focus. Right click, Shift+F10 and the Menu key
    // open it as before.
    private void OnListPressed(object? sender, DragPress press)
    {
        if (press.Pointer is DragPointer.Touch or DragPointer.Pen
            && ListsList.ContainerFromItem(press.Item) is ListViewItem row)
        {
            row.ContextFlyout = null;
            _menulessRow = row;
        }
    }

    private void RestoreRowMenu()
    {
        if (_menulessRow is { } row)
        {
            row.ContextFlyout = (FlyoutBase)Resources["ListMenu"];
            _menulessRow = null;
        }
    }

    // A hold lifted without a slide opens the List's context menu where the finger lifted, as a hold did before
    // Lists dragged by touch. The press has ended, so the row has its menu back.
    private void OnListLiftedAfterHold(object? sender, HeldLift lift)
    {
        if (ListsList.ContainerFromItem(lift.Item) is FrameworkElement { ContextFlyout: { } menu } row)
        {
            menu.ShowAt(row, new FlyoutShowOptions { Position = ListsList.TransformToVisual(row).TransformPoint(lift.At) });
        }
    }

    // A reload clears the Lists and adds them again, so whether the dragged List is gone is checked once it ends.
    private void QueueCancelDragOfGoneList()
    {
        if (_dragCheckQueued || ListDrag.Item is null)
        {
            return;
        }

        _dragCheckQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _dragCheckQueued = false;
            if (ListDrag.Item is ListItemViewModel dragged && Lists?.Lists.Any(l => l.Id == dragged.Id) != true)
            {
                ListDrag.Cancel();
            }
        });
    }

    // Any key makes the next click or selection the keyboard's. Esc during a drag reaches
    // TryShortcut through the window (MainWindow.TryShortcut), wherever the focus is.
    private void OnListPreviewKeyDown(object sender, KeyRoutedEventArgs e) => _keyInput = true;
}
