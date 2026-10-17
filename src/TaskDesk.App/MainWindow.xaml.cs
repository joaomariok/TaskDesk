using System.Windows;
using System.Windows.Input;
using TaskDesk.Core.Shell;
using TaskDesk.Presentation;

namespace TaskDesk.App;

/// <summary>
/// The composition root of the main window: hosts the navigation pane, the Task list area and the detail
/// pane (Views/), owns the resizable pane columns and the window-wide keyboard shortcuts.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel, ListsViewModel lists)
    {
        ViewModel = viewModel;
        Lists = lists;
        DataContext = viewModel;
        // A completed Task lingers struck through for a moment, unless Windows asks for less animation.
        viewModel.CompletionMoveDelay = () =>
            SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(400) : TimeSpan.Zero;
        InitializeComponent();
        // The quick-add box has the initial focus of the window.
        FocusManager.SetFocusedElement(this, TaskArea.QuickAddBox);
        Details.Initialize();
        TaskArea.Initialize(viewModel, lists);
        TaskArea.EnterRequested += (_, _) => Details.FocusTitle();
        viewModel.IsDetailDocked = Width > DockedAboveWidth;
        Navigation.Initialize(viewModel, lists);
        ApplyPaneLimits();

        // Open-Task counts change when Tasks are added, completed, un-completed, deleted or restored.
#pragma warning disable VSTHRD101 // Action event; a failed count reload surfaces on the dispatcher as before
        viewModel.OpenTaskCountsChanged += async (_, _) => await lists.LoadAsync();

        // A renamed or recoloured List: reload so the heading and the rows show its new name and colour.
        lists.ListEdited += async (_, _) => await viewModel.LoadAsync();
#pragma warning restore VSTHRD101
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsDetailPaneVisible) or nameof(MainViewModel.IsDetailDocked))
            {
                ShowDetailColumn(viewModel.IsDetailPaneVisible);
            }
        };
    }

    public MainViewModel ViewModel { get; }
    public ListsViewModel Lists { get; }

    // ---- Resizable panes ----

    // The pane docks beside the list in a window wider than this; at or below it, it overlays the list.
    private const double DockedAboveWidth = 1100;
    private const double OverlayMinWidth = 320;

    private double _detailWidth = PaneLayout.DetailLimits.Default;

    /// <summary>The sidebar and detail pane widths to save; a hidden detail pane measures 0.</summary>
    public IReadOnlyDictionary<string, double> MeasurePaneWidths() => new Dictionary<string, double>
    {
        [PaneLayout.Navigation] = Navigation.IsRail ? _navigationWidth : NavigationColumn.ActualWidth,
        [PaneLayout.Detail] = ViewModel.IsDetailPaneVisible
            ? ViewModel.IsDetailDocked ? DetailColumn.ActualWidth : _detailWidth
            : 0,
    };

    /// <summary>Shows the panes at the given widths (already within their limits).</summary>
    public void SetPaneWidths(double navigation, double detail)
    {
        _navigationWidth = navigation;
        if (!Navigation.IsRail)
        {
            NavigationColumn.Width = new GridLength(navigation);
        }

        _detailWidth = detail;
        ShowDetailColumn(ViewModel.IsDetailPaneVisible);
    }

    // ---- Icon rail ----

    private const double RailBreakpoint = 900;
    private const double RailWidth = 56;
    private double _navigationWidth = PaneLayout.NavigationLimits.Default;

    // Below 900 px the sidebar is an icon rail of a fixed width; wider, it is the full sidebar again at the
    // width the user had.
    private void UpdateSidebarMode()
    {
        var rail = ActualWidth < RailBreakpoint;
        if (rail == Navigation.IsRail)
        {
            return;
        }

        if (rail)
        {
            if (NavigationColumn.ActualWidth > 0)
            {
                _navigationWidth = PaneLayout.NavigationLimits.Clamp(NavigationColumn.ActualWidth);
            }

            NavigationColumn.MinWidth = RailWidth;
            NavigationColumn.MaxWidth = RailWidth;
            NavigationColumn.Width = new GridLength(RailWidth);
        }
        else
        {
            NavigationColumn.MinWidth = PaneLayout.NavigationLimits.Min;
            NavigationColumn.MaxWidth = PaneLayout.NavigationLimits.Max;
            NavigationColumn.Width = new GridLength(_navigationWidth);
        }

        SidebarSplitter.Visibility = rail ? Visibility.Collapsed : Visibility.Visible;
        Navigation.SetRail(rail);
    }

    private void ApplyPaneLimits()
    {
        NavigationColumn.MinWidth = PaneLayout.NavigationLimits.Min;
        NavigationColumn.MaxWidth = PaneLayout.NavigationLimits.Max;
        ShowDetailColumn(ViewModel.IsDetailPaneVisible);
    }

    // A hidden detail pane takes no room; shown again, it gets back the width it had.
    // Wider than DockedAboveWidth the pane sits in its own column; otherwise it overlays the list's right side.
    private void ShowDetailColumn(bool visible)
    {
        if (DetailColumn.ActualWidth > 0 && (!visible || !ViewModel.IsDetailDocked))
        {
            _detailWidth = PaneLayout.DetailLimits.Clamp(DetailColumn.ActualWidth);
        }

        var docked = ViewModel.IsDetailDocked;
        System.Windows.Controls.Grid.SetColumn(Details, docked ? 4 : 2);
        System.Windows.Controls.Grid.SetColumnSpan(Details, docked ? 1 : 3);
        Details.HorizontalAlignment = docked ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        Details.Width = docked ? double.NaN : Math.Max(_detailWidth, OverlayMinWidth);
        System.Windows.Controls.Panel.SetZIndex(Details, docked ? 0 : 10);
        DetailSplitter.Visibility = visible && docked ? Visibility.Visible : Visibility.Collapsed;

        if (visible && docked)
        {
            DetailColumn.MinWidth = PaneLayout.DetailLimits.Min;
            DetailColumn.MaxWidth = PaneLayout.DetailLimits.Max;
            DetailColumn.Width = new GridLength(_detailWidth);
        }
        else
        {
            DetailColumn.MinWidth = 0;
            DetailColumn.Width = new GridLength(0);
        }
    }

    // One resize path: below 900 px the sidebar is an icon rail; above 1100 px the detail pane docks.
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSidebarMode();
        ViewModel.IsDetailDocked = e.NewSize.Width > DockedAboveWidth;
    }

    // Esc closes the detail pane and returns focus to its Task's row. It is a bubbling handler, so a
    // dropdown or date picker that is open closes first.
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel.IsDetailPaneVisible && ViewModel.DetailTask is { } row)
        {
            ViewModel.CloseDetailPaneCommand.Execute(null);
            TaskArea.FocusRow(row);
            e.Handled = true;
        }
    }

    // F2 edits the title of the selected Task in the detail pane.
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            TaskArea.FocusSearch();
            e.Handled = true;
            return;
        }

        // Ctrl+N only moves focus to quick add; it changes no selection, search, view or pane.
        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            TaskArea.FocusQuickAdd();
            e.Handled = true;
            return;
        }

        // Esc clears the search and restores the view.
        if (e.Key == Key.Escape && ViewModel.SearchText.Length > 0)
        {
            ViewModel.ClearSearchCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2 && ViewModel.SelectedTask is not null)
        {
            ViewModel.OpenDetailPane();
            Details.FocusTitle();
            e.Handled = true;
        }
    }
}
