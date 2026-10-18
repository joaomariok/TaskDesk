using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
    private bool _syncing;
    private bool _countRefreshQueued;

    /// <param name="viewModel">The page's view model: Smart Views, counts and Tags.</param>
    /// <param name="lists">The Lists section; null shows no Lists.</param>
    /// <param name="dialogs">Confirms deleting a Tag; null deletes without asking (tests of other parts).</param>
    public NavigationPane(MainViewModel viewModel, ListsViewModel? lists = null, Dialogs.DialogService? dialogs = null)
    {
        ViewModel = viewModel;
        Lists = lists;
        _dialogs = dialogs;
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
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        // Adding, completing, deleting or restoring a Task changes the counts.
        viewModel.OpenTaskCountsChanged += (_, _) => QueueCountRefresh();
        Pane.Loaded += (_, _) => ExposeToAutomation();
        Loaded += (_, _) =>
        {
            SyncSelection();
            QueueCountRefresh();
        };
        FollowSplitterDragsWithoutMoves();
    }

    public MainViewModel ViewModel { get; }

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

    // The splitter (a toolkit PropertySizer) resizes from manipulation deltas, which need pointer moves while the
    // button is down, and it does not capture the pointer. Input that jumps between press and release (UI
    // Automation's mouse, a touchpad that reports no move in between) then lands outside the splitter and never
    // resizes. Capturing on press and resizing on release by the distance travelled covers that; when the
    // manipulation already resized the pane, release leaves it alone.
    private void FollowSplitterDragsWithoutMoves()
    {
        double? pressedAt = null;
        double startWidth = 0;
        var manipulated = false;
        SidebarSplitter.AddHandler(PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (!e.GetCurrentPoint(SidebarSplitter).Properties.IsLeftButtonPressed)
            {
                return;
            }

            pressedAt = e.GetCurrentPoint(null).Position.X;
            startWidth = SidebarWidth;
            manipulated = false;
            SidebarSplitter.CapturePointer(e.Pointer);
        }), handledEventsToo: true);
        SidebarSplitter.AddHandler(ManipulationDeltaEvent,
            new ManipulationDeltaEventHandler((_, _) => manipulated = true), handledEventsToo: true);
        SidebarSplitter.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (pressedAt is { } start && !manipulated)
            {
                SidebarWidth = startWidth + e.GetCurrentPoint(null).Position.X - start;
            }

            pressedAt = null;
            SidebarSplitter.ReleasePointerCapture(e.Pointer);
        }), handledEventsToo: true);
        SidebarSplitter.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => pressedAt = null),
            handledEventsToo: true);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CurrentView) or nameof(MainViewModel.TagFilter))
        {
            SyncSelection();
        }
    }

    // Shows which page is current: a Smart View, a Tag, or the Default List.
    private void SyncSelection()
    {
        _syncing = true;
        try
        {
            if (ViewModel.CurrentView is { } view)
            {
                Pane.SelectedItem = _views.First(p => p.Value == view).Key;
            }
            else if (Pane.SelectedItem is NavigationViewItem item && _views.ContainsKey(item))
            {
                Pane.SelectedItem = null;
            }

            SyncListAndTagSelection();
        }
        finally
        {
            _syncing = false;
        }
    }

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_syncing && args.SelectedItemContainer is NavigationViewItem item && _views.TryGetValue(item, out var view)
            && ViewModel.CurrentView != view)
        {
            await ViewModel.ShowSmartViewAsync(view);
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
        foreach (var element in Descendants(Pane).OfType<FrameworkElement>())
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

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
