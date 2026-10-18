using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TaskDesk.Core.Shell;
using TaskDesk.Presentation;
using Windows.System;

namespace TaskDesk.Views;

/// <summary>
/// The main window's page (<see cref="MainView"/>) with the detail pane of the selected Task beside it or over
/// it. Wider than <see cref="PaneLayout.DetailDockedAboveWidth"/> the pane docks in its own column and the
/// detail splitter resizes it; otherwise it overlays the page's right side. A click or Enter on a row opens the
/// pane, F2 opens it on the selected Task, Esc closes it and returns focus to the Task's row.
/// </summary>
public sealed partial class DetailDock : UserControl
{
    private double _detailWidth = PaneLayout.DetailLimits.Default;

    // Where a press on the splitter started, so a drag the splitter itself did not apply still resizes the pane.
    private (double X, double Width)? _splitterPress;

    public DetailDock(MainView page)
    {
        Page = page;
        ViewModel = page.ViewModel;
        InitializeComponent();
        PageHost.Child = page;
        Pane = new DetailPane(ViewModel);
        AutomationProperties.SetAutomationId(Pane, "DetailPane");
        DockRoot.Children.Add(Pane);
        Pane.SizeChanged += (_, e) => RememberDockedWidth(e.NewSize.Width);

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        page.TaskList.DetailFocusRequested += (_, _) => Pane.FocusTitle();
        // The splitter's own drag runs on manipulation events; a press and release with a single jump in
        // between (some automation input) produces none, so the release applies the drag itself.
        DetailSplitter.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSplitterPressed), true);
        DockRoot.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnDockPointerReleased), true);
        DetailSplitter.PointerCaptureLost += (_, _) => _splitterPress = null;
        ShowPane();
    }

    public MainView Page { get; }

    public MainViewModel ViewModel { get; }

    /// <summary>The detail pane (AutomationId DetailPane).</summary>
    public DetailPane Pane { get; }

    /// <summary>True while the pane is shown.</summary>
    public bool IsPaneShown => ViewModel.IsDetailPaneVisible;

    /// <summary>
    /// The width of the docked pane, within <see cref="PaneLayout.DetailLimits"/>; kept while the pane is
    /// hidden or overlays the list, where it is at least <see cref="PaneLayout.DetailOverlayMinWidth"/> wide.
    /// </summary>
    public double DetailWidth
    {
        get => _detailWidth;
        set
        {
            _detailWidth = PaneLayout.DetailLimits.Clamp(value);
            ShowPane();
        }
    }

    /// <summary>
    /// Runs the detail pane's shortcut for a key: F2 opens the pane on the selected Task and edits its title;
    /// Esc closes the pane and returns focus to its Task's row. Returns false when there is nothing to do.
    /// </summary>
    public bool TryShortcut(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        if (modifiers != VirtualKeyModifiers.None)
        {
            return false;
        }

        if (key == VirtualKey.F2 && ViewModel.SelectedTask is not null)
        {
            ViewModel.OpenDetailPane();
            Pane.FocusTitle();
            return true;
        }

        if (key == VirtualKey.Escape && ViewModel.IsDetailPaneVisible && ViewModel.DetailTask is { } row)
        {
            ViewModel.CloseDetailPaneCommand.Execute(null);
            Page.TaskList.FocusRow(row);
            return true;
        }

        return false;
    }

    // Esc bubbles up from the focused element, so an open date picker or dropdown closes first.
    private void OnDockKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && TryShortcut(VirtualKey.Escape, VirtualKeyModifiers.None))
        {
            e.Handled = true;
        }
    }

    private void OnF2Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = TryShortcut(VirtualKey.F2, VirtualKeyModifiers.None);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsDetailPaneVisible) or nameof(MainViewModel.IsDetailDocked))
        {
            ShowPane();
        }
    }

    // A hidden pane takes no room; shown again, it has the width it had. Docked, it sits in its own column
    // beside the splitter; overlaid, it covers the page's right side and the splitter hides.
    private void ShowPane()
    {
        var visible = ViewModel.IsDetailPaneVisible;
        var docked = ViewModel.IsDetailDocked;
        Pane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(Pane, docked ? 2 : 0);
        Grid.SetColumnSpan(Pane, docked ? 1 : 3);
        Pane.HorizontalAlignment = docked ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        Pane.Width = docked ? double.NaN : PaneLayout.DetailOverlayWidth(_detailWidth);
        Canvas.SetZIndex(Pane, docked ? 0 : 10);
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

    // The splitter changes the docked column; remember its width for saving, for the overlay and for later.
    private void RememberDockedWidth(double width)
    {
        if (ViewModel.IsDetailPaneVisible && ViewModel.IsDetailDocked && width > 0)
        {
            _detailWidth = PaneLayout.DetailLimits.Clamp(width);
        }
    }

    private void OnSplitterPressed(object sender, PointerRoutedEventArgs e) =>
        _splitterPress = (e.GetCurrentPoint(DockRoot).Position.X, DetailColumn.ActualWidth);

    private void OnDockPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_splitterPress is not { } press)
        {
            return;
        }

        _splitterPress = null;
        var dx = e.GetCurrentPoint(DockRoot).Position.X - press.X;
        // The splitter's own drag already moved the column: nothing left to do.
        if (Math.Abs(DetailColumn.ActualWidth - press.Width) >= 1 || Math.Abs(dx) < 1
            || !ViewModel.IsDetailPaneVisible || !ViewModel.IsDetailDocked)
        {
            return;
        }

        DetailWidth = PaneLayout.DraggedDetailWidth(press.Width, dx);
    }
}
