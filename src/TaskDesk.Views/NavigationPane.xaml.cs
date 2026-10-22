using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;

namespace TaskDesk.Views;

/// <summary>
/// The sidebar: a native <see cref="NavigationView"/> with the Smart Views and their open-Task counts (Overdue's
/// in red), around the page it shows (<see cref="PageContent"/>). Below 900 px it is the icon rail. Ctrl+1 to
/// Ctrl+5 switch Smart Views; the sidebar splitter resizes the pane.
/// </summary>
public sealed partial class NavigationPane : UserControl
{
    private readonly Dictionary<NavigationViewItem, SmartView> _views;
    private bool _countRefreshQueued;

    /// <param name="viewModel">The page's view model: Smart Views, counts and Tags.</param>
    /// <param name="dialogs">Confirms deleting a Tag (Lists confirm through their own dialogs).</param>
    /// <param name="lists">The sidebar with its Lists, over <paramref name="viewModel"/>; null shows no Lists.</param>
    public NavigationPane(MainViewModel viewModel, Dialogs.DialogService dialogs, SidebarViewModel? lists = null)
    {
        ViewModel = viewModel;
        Lists = lists;
        Sidebar = lists ?? SidebarViewModel.WithoutLists(viewModel);
        _dialogs = dialogs;
        ListDrag = new ReorderDrag(this, viewModel.Clock);
        _dragScroll = new DragScroller(ListDrag);
        InitializeComponent();
        InitializeListsAndTags();
        _views = new()
        {
            [TodayItem] = SmartView.Today,
            [UpcomingItem] = SmartView.Upcoming,
            [OverdueItem] = SmartView.Overdue,
            [AllItem] = SmartView.All,
            [CompletedItem] = SmartView.Completed,
        };
        Pane.OpenPaneLength = PaneLayout.NavigationLimits.Default;
        Sidebar.PropertyChanged += OnSidebarPropertyChanged;
        // Adding, completing, deleting or restoring a Task changes the counts.
        viewModel.OpenTaskCountsChanged += (_, _) => QueueCountRefresh();
        Pane.Loaded += (_, _) => ExposeToAutomation();
        Loaded += (_, _) =>
        {
            SyncSelection();
            QueueCountRefresh();
        };
    }

    public MainViewModel ViewModel { get; }

    /// <summary>The entry the pane shows as selected, and the call that opens the entry the owner selects.</summary>
    public SidebarViewModel Sidebar { get; }

    /// <summary>The page shown beside the pane (the main window's <see cref="MainView"/>).</summary>
    public UIElement? PageContent
    {
        get => PageHost.Child;
        set => PageHost.Child = value;
    }

    /// <summary>The width of the full sidebar, within <see cref="PaneLayout.NavigationLimits"/>; kept while it is the rail.</summary>
    public double SidebarWidth
    {
        get => Pane.OpenPaneLength;
        set => Pane.OpenPaneLength = PaneLayout.NavigationLimits.Clamp(value);
    }

    /// <summary>True while the sidebar shows as the icon rail (the compact pane).</summary>
    public bool IsRail { get; private set; }

    public static double MinimumWidth => PaneLayout.NavigationLimits.Min;

    public static double MaximumWidth => PaneLayout.NavigationLimits.Max;

    /// <summary>Counts of zero are not shown.</summary>
    public static Visibility ShownAboveZero(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Switches between the full sidebar and the icon rail. The rail's hamburger opens the full pane as an
    /// overlay; the splitter only shows beside the full sidebar.
    /// </summary>
    public void SetRail(bool rail)
    {
        if (rail == IsRail)
        {
            return;
        }

        IsRail = rail;
        Pane.PaneDisplayMode = rail ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Left;
        Pane.IsPaneToggleButtonVisible = rail;
        Pane.IsPaneOpen = !rail;
        SidebarSplitter.Visibility = rail ? Visibility.Collapsed : Visibility.Visible;
        ShowCompact(rail && !Pane.IsPaneOpen);
    }

    private void OnSidebarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SidebarViewModel.SelectedSmartView) or nameof(SidebarViewModel.SelectedList)
            or nameof(SidebarViewModel.SelectedTag))
        {
            SyncSelection();
        }
    }

    // Sets the four selectors (Smart Views, Lists, Tags and the rail's Tags) to the sidebar's selected entry. The
    // selection-changed events this raises open nothing: each selector then holds the entry already selected.
    private void SyncSelection()
    {
        if (Sidebar.SelectedSmartView is { } view)
        {
            Pane.SelectedItem = _views.First(p => p.Value == view).Key;
        }
        else if (Pane.SelectedItem is NavigationViewItem item && _views.ContainsKey(item))
        {
            Pane.SelectedItem = null;
        }

        TagList.SelectedItem = Sidebar.SelectedTag;
        RailTagList.SelectedItem = Sidebar.SelectedTag;
        ListsList.SelectedItem = Sidebar.SelectedList;
    }

    // Selecting a Smart View opens it; the one already shown opens nothing.
    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item && _views.TryGetValue(item, out var view))
        {
            await Sidebar.OpenAsync(new SidebarEntry.SmartViewEntry(view));
        }
    }

    // Ctrl+1 to Ctrl+5, on the Smart View items: switch to that Smart View from anywhere in the window.
    private void OnAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (args.Element is NavigationViewItem item && _views.TryGetValue(item, out var view))
        {
            ViewModel.SelectSmartViewCommand.Execute(view);
            args.Handled = true;
        }
    }

    // Coalesces bursts of changes into one recount, after the work at hand.
    private void QueueCountRefresh()
    {
        if (_countRefreshQueued)
        {
            return;
        }

        _countRefreshQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = RefreshCountsAsync());
    }

    private async Task RefreshCountsAsync()
    {
        _countRefreshQueued = false;
        try
        {
            await ViewModel.RefreshSmartViewCountsAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The counts stay as they were; the next change refreshes them again.
        }
    }

    // The UI tests find the pane, its Smart Views and the rail's hamburger by these ids, as in the WPF app.
    // The Lists and Tags (the pane footer) move up under the Smart Views.
    private void ExposeToAutomation()
    {
        foreach (var element in VisualTreeWalk.Descendants(Pane).OfType<FrameworkElement>())
        {
            switch (element.Name)
            {
                case "MenuItemsScrollViewer":
                    AutomationProperties.SetAutomationId(element, "NavigationPane");
                    break;
                case "MenuItemsHost":
                    AutomationProperties.SetAutomationId(element, "SmartViewList");
                    AutomationProperties.SetName(element, "Smart Views");
                    break;
                case "TogglePaneButton":
                    AutomationProperties.SetAutomationId(element, "SidebarExpandButton");
                    break;
                case "ItemsContainerGrid" when element is Grid grid:
                    FitFooterUnderMenuItems(grid);
                    break;
            }
        }
    }
}
