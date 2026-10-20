using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using Windows.System;

namespace TaskDesk.Views;

/// <summary>
/// The main window's content: the list header (page heading with Filter and search beside it, Filter bar,
/// quick add, empty state) over the Task list area of the page the view model shows.
/// </summary>
public sealed partial class MainView : UserControl
{
    private static readonly string[] PriorityChoices = ["Any priority", "Low or higher", "Medium or higher", "High"];

    private const double MaxNameMinWidth = 160;
    private const double MinSearchWidth = 160;
    private const double MaxSearchWidth = 280;

    private bool _syncingPriorityFilter;

    public MainView(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        TaskList = new TaskListView(viewModel);
        Grid.SetRow(TaskList, 1);
        RootGrid.Children.Add(TaskList);

        PriorityFilterBox.ItemsSource = PriorityChoices;
        // Shows the view model's Filter. Not a change, so no reload: the shell loads the page once the database
        // is ready.
        _syncingPriorityFilter = true;
        PriorityFilterBox.SelectedIndex = (int)(viewModel.MinPriorityFilter ?? Priority.None);
        _syncingPriorityFilter = false;
        PageHeading.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => FitHeadingRow());
        HeadingRow.SizeChanged += (_, _) => FitHeadingRow();
        FilterButton.SizeChanged += (_, _) => FitHeadingRow();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.FocusQuickAddRequested += (_, _) => FocusQuickAdd();
    }

    /// <summary>The one Task list (Overdue, open and "Completed (N)" groups) and the Undo bar.</summary>
    public TaskListView TaskList { get; }

    public MainViewModel ViewModel { get; }

    /// <summary>Gives quick add keyboard focus, as the window does at start-up and on Ctrl+N.</summary>
    public void FocusQuickAdd()
    {
        // On the Completed Smart View quick add has only just been shown; lay it out so it can take focus.
        QuickAddBox.UpdateLayout();
        QuickAddBox.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Focuses the search box with its text selected (Ctrl+F). An overlaid detail pane hides the search box, so it
    /// is closed first.
    /// </summary>
    public void FocusSearch()
    {
        if (!ViewModel.HasHeadingTools)
        {
            ViewModel.CloseDetailPaneCommand.Execute(null);
            // The search box has only just been shown; lay it out so it can take focus.
            SearchBox.UpdateLayout();
        }

        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
    }

    /// <summary>
    /// Runs the list header shortcut for a key: Ctrl+F searches, Ctrl+N goes to quick add, Esc clears the search
    /// and Enter in quick add adds the Task. Returns false when the key is not a shortcut or there is nothing to do,
    /// and while the page takes no input (the exit is under way).
    /// </summary>
    public bool TryShortcut(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        if (!IsEnabled)
        {
            return false;
        }

        switch (key, modifiers)
        {
            case (VirtualKey.F, VirtualKeyModifiers.Control):
                FocusSearch();
                return true;
            case (VirtualKey.N, VirtualKeyModifiers.Control):
                // On the Completed Smart View this shows the Default List first, then asks for focus.
                ViewModel.FocusQuickAddCommand.Execute(null);
                return true;
            case (VirtualKey.Escape, VirtualKeyModifiers.None) when ViewModel.SearchText.Length > 0:
                ViewModel.ClearSearchCommand.Execute(null);
                return true;
            case (VirtualKey.Enter, VirtualKeyModifiers.None) when HasFocus(QuickAddBox):
                return Run(ViewModel.AddTaskCommand);
            default:
                return false;
        }
    }

    /// <summary>The text of a Tag chip: "#" and the Tag's name.</summary>
    public static string TagText(Tag tag) => "#" + tag.Name;

    private static bool Run(ICommand command)
    {
        if (!command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    private bool HasFocus(Control control) =>
        XamlRoot is { } root && ReferenceEquals(FocusManager.GetFocusedElement(root), control);

    private void OnAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = TryShortcut(sender.Key, sender.Modifiers);

    // Esc bubbles here only when nothing inside (an open drop-down, say) used it first.
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && TryShortcut(e.Key, KeyboardModifiers.Current()))
        {
            e.Handled = true;
        }
    }

    private void OnQuickAddKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && TryShortcut(e.Key, KeyboardModifiers.Current()))
        {
            e.Handled = true;
        }
    }

    // ---- Search and Filters ----

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnPriorityFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingPriorityFilter || PriorityFilterBox.SelectedIndex < 0)
        {
            return;
        }

        await ViewModel.SetMinPriorityFilterAsync((Priority)PriorityFilterBox.SelectedIndex);
    }

    // A chip's template cannot reach the page's view model, so its Command is set here: a click, Space and a
    // screen reader's Toggle then all run it once, with the chip's Tag.
    private void OnFilterTagChipLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is CommandToggleButton { Command: null } chip)
        {
            chip.Command = ViewModel.ToggleFilterTagCommand;
        }
    }

    // The name fits first. Of the room left after Filter and the two gaps, search takes up to 280 px but leaves the
    // name what it needs: its own width, up to 160 px. Search shrinks (to 160 px) only for a name already that small.
    private void FitHeadingRow()
    {
        if (!ViewModel.HasHeadingTools)
        {
            SearchColumn.Width = new GridLength(0);
            return;
        }

        PageHeading.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var nameNeeds = Math.Min(PageHeading.DesiredSize.Width, MaxNameMinWidth);
        PageHeading.InvalidateMeasure(); // the grid measures it again with its column's width
        var room = HeadingRow.ActualWidth - FilterButton.ActualWidth - 2 * HeadingRow.ColumnSpacing;
        var search = Math.Clamp(room - nameNeeds, MinSearchWidth, MaxSearchWidth);
        if (Math.Abs(SearchColumn.Width.Value - search) > 0.01 || SearchColumn.Width.GridUnitType != GridUnitType.Pixel)
        {
            SearchColumn.Width = new GridLength(search);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HasHeadingTools))
        {
            FitHeadingRow();
        }
        else if (e.PropertyName == nameof(MainViewModel.MinPriorityFilter))
        {
            _syncingPriorityFilter = true;
            PriorityFilterBox.SelectedIndex = (int)(ViewModel.MinPriorityFilter ?? Priority.None);
            _syncingPriorityFilter = false;
        }
    }
}
