using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using Windows.System;

namespace TaskDesk.Views;

/// <summary>
/// The main window's content: the list header (page heading, its command bar with Filter and the List's
/// settings, Filter bar, search,
/// quick add, empty state) over the Task list area of the page the view model shows.
/// </summary>
public sealed partial class MainView : UserControl
{
    // The heading's "⋯" overflow keeps the id and name of the List menu button it replaced.
    private const string ListMenuId = "ListMenuButton";
    private const string ListMenuName = "List settings";

    private static readonly string[] PriorityChoices = ["Any priority", "Low or higher", "Medium or higher", "High"];

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
    }

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.MinPriorityFilter))
        {
            _syncingPriorityFilter = true;
            PriorityFilterBox.SelectedIndex = (int)(ViewModel.MinPriorityFilter ?? Priority.None);
            _syncingPriorityFilter = false;
        }
    }

    // ---- The heading's command bar: Filter, and the "⋯" overflow with the shown List's settings ----

    /// <summary>The overflow holds only List commands, so it shows on a List's page only.</summary>
    public static CommandBarOverflowButtonVisibility ListOverflow(bool isListPage) =>
        isListPage ? CommandBarOverflowButtonVisibility.Visible : CommandBarOverflowButtonVisibility.Collapsed;

    // The overflow button is a template part with no property of its own: name it here, so a screen reader and
    // the UI suite find "List settings" as before. CommandBar names it "More options" when its template applies
    // and again each time it opens or closes, so the name is put back whenever CommandBar changes it.
    private void OnHeadingCommandsLoaded(object sender, RoutedEventArgs e)
    {
        if (FindByName<Button>((DependencyObject)sender, "MoreButton") is not { } more
            || AutomationProperties.GetAutomationId(more) == ListMenuId)
        {
            return;
        }

        AutomationProperties.SetAutomationId(more, ListMenuId);
        NameListMenu(more);
        more.RegisterPropertyChangedCallback(AutomationProperties.NameProperty, (d, _) => NameListMenu((Button)d));
        more.RegisterPropertyChangedCallback(ToolTipService.ToolTipProperty, (d, _) => NameListMenu((Button)d));
    }

    private static void NameListMenu(Button more)
    {
        if (AutomationProperties.GetName(more) != ListMenuName)
        {
            AutomationProperties.SetName(more, ListMenuName);
        }

        if (ToolTipService.GetToolTip(more) as string != ListMenuName)
        {
            ToolTipService.SetToolTip(more, ListMenuName);
        }
    }

    private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if ((child as T)?.Name == name)
            {
                return (T)child;
            }

            if (FindByName<T>(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private ListItemViewModel? ShownList => Lists?.ListOrDefault(ViewModel.CurrentListId);

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
        // None's empty Tag arrives as null, not "", so read it as a nullable string.
        if (Lists is { } lists && ShownList is { } list && sender is MenuFlyoutItem item)
        {
            await lists.SetColorAsync(list, item.Tag as string);
        }
    }
}
