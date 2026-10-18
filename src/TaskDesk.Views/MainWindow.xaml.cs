using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using TaskDesk.Core.Shell;
using TaskDesk.Presentation;
using Windows.System;

namespace TaskDesk.Views;

/// <summary>
/// The main window. It only hosts <see cref="MainView"/>; the shell owns its lifecycle (show, hide to the
/// tray, saved size and position) through <see cref="Window.AppWindow"/>.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel, ListsViewModel? lists = null)
    {
        InitializeComponent();
        View = new MainView(viewModel);
        Lists = lists;
        View.Lists = lists;
        // The page with the detail pane beside it or over it.
        Details = new DetailDock(View);
        // The sidebar's NavigationView hosts the page as its content. Deleting a Tag asks on this window.
        Navigation = new NavigationPane(viewModel, lists, new Dialogs.DialogService(() => this)) { PageContent = Details };
        if (lists is not null)
        {
#pragma warning disable VSTHRD101 // Event handlers; a failed reload reaches the app's UnhandledException log
            // Open-Task counts change when Tasks are added, completed, un-completed, deleted or restored.
            viewModel.OpenTaskCountsChanged += async (_, _) => await lists.LoadAsync();
            // A renamed or recoloured List: reload so the heading and the rows show its new name and colour.
            lists.ListEdited += async (_, _) => await viewModel.LoadAsync();
#pragma warning restore VSTHRD101
        }

        Content = Navigation;
        AddSettingsAccelerator(viewModel);
        // The quick-add box has the initial focus of the window.
        View.Loaded += (_, _) => View.FocusQuickAdd();
    }

    public MainView View { get; }

    /// <summary>The sidebar's Lists, or null when the window shows none.</summary>
    public ListsViewModel? Lists { get; }

    /// <summary>The sidebar with the Smart Views, around <see cref="View"/>.</summary>
    public NavigationPane Navigation { get; }

    /// <summary>The detail pane of the selected Task, docked beside <see cref="View"/> or over it.</summary>
    public DetailDock Details { get; }

    /// <summary>
    /// The pane widths to save (<see cref="PaneLayout"/>): the full sidebar's width, also while it is the rail,
    /// and the detail pane's docked width; a hidden detail pane measures 0 and keeps its saved width.
    /// </summary>
    public IReadOnlyDictionary<string, double> MeasurePaneWidths() => new Dictionary<string, double>
    {
        [PaneLayout.Navigation] = Navigation.SidebarWidth,
        [PaneLayout.Detail] = Details.IsPaneShown ? Details.DetailWidth : 0,
    };

    /// <summary>Shows the panes at their saved widths, within their limits; the defaults without a saved layout.</summary>
    public void SetPaneWidths(WindowLayout? layout)
    {
        Navigation.SidebarWidth = PaneLayout.WidthOf(layout, PaneLayout.Navigation);
        Details.DetailWidth = PaneLayout.WidthOf(layout, PaneLayout.Detail);
    }

    public MainViewModel ViewModel => View.ViewModel;

    // Ctrl+comma opens Settings from anywhere in the window; no tooltip names it.
    private void AddSettingsAccelerator(MainViewModel viewModel)
    {
        const VirtualKey comma = (VirtualKey)0xBC; // VK_OEM_COMMA: VirtualKey has no name for it.
        var accelerator = new KeyboardAccelerator { Key = comma, Modifiers = VirtualKeyModifiers.Control };
        accelerator.Invoked += (_, args) =>
        {
            args.Handled = true;
            viewModel.OpenSettingsCommand.Execute(null);
        };
        Navigation.KeyboardAccelerators.Add(accelerator);
        Navigation.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
    }
}
