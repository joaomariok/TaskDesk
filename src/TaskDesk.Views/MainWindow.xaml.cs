using Microsoft.UI.Xaml;
using TaskDesk.Core.Shell;
using TaskDesk.Presentation;

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
        // The sidebar's NavigationView hosts the page as its content. Deleting a Tag asks on this window.
        Navigation = new NavigationPane(viewModel, lists, new Dialogs.DialogService(() => this)) { PageContent = View };
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
        // The quick-add box has the initial focus of the window.
        View.Loaded += (_, _) => View.FocusQuickAdd();
    }

    public MainView View { get; }

    /// <summary>The sidebar's Lists, or null when the window shows none.</summary>
    public ListsViewModel? Lists { get; }

    /// <summary>The sidebar with the Smart Views, around <see cref="View"/>.</summary>
    public NavigationPane Navigation { get; }

    /// <summary>The pane widths to save (<see cref="PaneLayout"/>): the full sidebar's width, also while it is the rail.</summary>
    public IReadOnlyDictionary<string, double> MeasurePaneWidths() => new Dictionary<string, double>
    {
        [PaneLayout.Navigation] = Navigation.SidebarWidth,
    };

    /// <summary>Shows the panes at their saved widths, within their limits; the defaults without a saved layout.</summary>
    public void SetPaneWidths(WindowLayout? layout)
    {
        Navigation.SidebarWidth = PaneLayout.WidthOf(layout, PaneLayout.Navigation);
    }

    public MainViewModel ViewModel => View.ViewModel;
}
