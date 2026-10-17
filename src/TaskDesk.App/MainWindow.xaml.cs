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
        InitializeComponent();
        // The quick-add box has the initial focus of the window.
        FocusManager.SetFocusedElement(this, TaskArea.QuickAddBox);
        Details.Initialize();
        TaskArea.Initialize(viewModel);
        Navigation.Initialize(viewModel, lists);
        ApplyPaneLimits();

        // Open-Task counts change when Tasks are added, completed, un-completed, deleted or restored.
#pragma warning disable VSTHRD101 // Action event; a failed count reload surfaces on the dispatcher as before
        viewModel.OpenTaskCountsChanged += async (_, _) => await lists.LoadAsync();
#pragma warning restore VSTHRD101
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsDetailPaneVisible))
            {
                ShowDetailColumn(viewModel.IsDetailPaneVisible);
            }
        };
    }

    public MainViewModel ViewModel { get; }
    public ListsViewModel Lists { get; }

    // ---- Resizable panes ----

    private double _detailWidth = PaneLayout.DetailLimits.Default;

    /// <summary>The sidebar and detail pane widths to save; a hidden detail pane measures 0.</summary>
    public IReadOnlyDictionary<string, double> MeasurePaneWidths() => new Dictionary<string, double>
    {
        [PaneLayout.Navigation] = NavigationColumn.ActualWidth,
        [PaneLayout.Detail] = ViewModel.IsDetailPaneVisible ? DetailColumn.ActualWidth : 0,
    };

    /// <summary>Shows the panes at the given widths (already within their limits).</summary>
    public void SetPaneWidths(double navigation, double detail)
    {
        NavigationColumn.Width = new GridLength(navigation);
        _detailWidth = detail;
        ShowDetailColumn(ViewModel.IsDetailPaneVisible);
    }

    private void ApplyPaneLimits()
    {
        NavigationColumn.MinWidth = PaneLayout.NavigationLimits.Min;
        NavigationColumn.MaxWidth = PaneLayout.NavigationLimits.Max;
        ShowDetailColumn(ViewModel.IsDetailPaneVisible);
    }

    // A hidden detail pane takes no room; shown again, it gets back the width it had.
    private void ShowDetailColumn(bool visible)
    {
        if (visible)
        {
            DetailColumn.MinWidth = PaneLayout.DetailLimits.Min;
            DetailColumn.MaxWidth = PaneLayout.DetailLimits.Max;
            DetailColumn.Width = new GridLength(_detailWidth);
        }
        else
        {
            if (DetailColumn.ActualWidth > 0)
            {
                _detailWidth = PaneLayout.DetailLimits.Clamp(DetailColumn.ActualWidth);
            }

            DetailColumn.MinWidth = 0;
            DetailColumn.Width = new GridLength(0);
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

        // Esc clears the search and restores the view.
        if (e.Key == Key.Escape && ViewModel.SearchText.Length > 0)
        {
            ViewModel.ClearSearchCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2 && ViewModel.SelectedTask is not null)
        {
            ViewModel.IsDetailPaneOpen = true;
            Details.FocusTitle();
            e.Handled = true;
        }
    }
}
