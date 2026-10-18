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
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace TaskDesk.Views;

/// <summary>
/// The main window's content: the list header (page heading, List menu, Filter button and Filter bar, search,
/// quick add, empty state) over the Task list area of the page the view model shows.
/// </summary>
public sealed partial class MainView : UserControl
{
    // Search reloads once typing pauses, so each keystroke does not query the database.
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);

    private static readonly string[] PriorityChoices = ["Any priority", "Low or higher", "Medium or higher", "High"];

    private DispatcherQueueTimer? _searchTimer;
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
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.FocusQuickAddRequested += (_, _) => FocusQuickAdd();
        // A search still waiting for typing to pause is dropped with the page: run after its window closed,
        // the reload would query a database the shell may already have let go of. MainWindow also drops it as it
        // closes, since Unloaded comes only after Close has returned.
        Unloaded += (_, _) => CancelPendingSearch();
    }

    /// <summary>True while a search waits for typing to pause before it reloads the page.</summary>
    public bool IsSearchPending => _searchTimer?.IsRunning == true;

    /// <summary>Drops a search still waiting for typing to pause, so it never reloads the page.</summary>
    public void CancelPendingSearch() => _searchTimer?.Stop();

    /// <summary>The Overdue, open and "Completed (N)" Task lists and the Undo bar.</summary>
    public TaskListView TaskList { get; }

    public MainViewModel ViewModel { get; }

    /// <summary>The Lists the List menu renames and colours; the menu does nothing until the shell sets it.</summary>
    public ListsViewModel? Lists { get; set; }

    /// <summary>Gives quick add keyboard focus, as the window does at start-up and on Ctrl+N.</summary>
    public void FocusQuickAdd()
    {
        // On the Completed Smart View quick add has only just been shown; lay it out so it can take focus.
        QuickAddBox.UpdateLayout();
        QuickAddBox.Focus(FocusState.Programmatic);
    }

    /// <summary>Focuses the search box with its text selected (Ctrl+F).</summary>
    public void FocusSearch()
    {
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
    }

    /// <summary>
    /// Runs the list header shortcut for a key: Ctrl+F searches, Ctrl+N goes to quick add, Esc clears the search
    /// and Enter in quick add adds the Task. Returns false when the key is not a shortcut or there is nothing to do.
    /// </summary>
    public bool TryShortcut(VirtualKey key, VirtualKeyModifiers modifiers)
    {
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

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_searchTimer is null)
        {
            _searchTimer = DispatcherQueue.CreateTimer();
            _searchTimer.Interval = SearchDelay;
            _searchTimer.IsRepeating = false;
            _searchTimer.Tick += (_, _) => ViewModel.ApplySearchCommand.Execute(null);
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.MinPriorityFilter))
        {
            _syncingPriorityFilter = true;
            PriorityFilterBox.SelectedIndex = (int)(ViewModel.MinPriorityFilter ?? Priority.None);
            _syncingPriorityFilter = false;
        }
    }

    // ---- The List's menu beside the heading ----

    private ListItemViewModel? ShownList => Lists?.Lists.FirstOrDefault(l => l.IsDefault);

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnListRenameClick(object sender, RoutedEventArgs e)
    {
        if (Lists is { } lists && ShownList is { } list)
        {
            await lists.RenameCommand.ExecuteAsync(list);
        }
    }

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnListColourClick(object sender, RoutedEventArgs e)
    {
        if (Lists is { } lists && ShownList is { } list && sender is MenuFlyoutItem { Tag: string hex })
        {
            await lists.SetColorAsync(list, hex);
        }
    }
}
