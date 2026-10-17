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
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        View = new MainView(viewModel);
        // The sidebar's NavigationView hosts the page as its content.
        Navigation = new NavigationPane(viewModel) { PageContent = View };
        Content = Navigation;
        // The quick-add box has the initial focus of the window.
        View.Loaded += (_, _) => View.FocusQuickAdd();
    }

    public MainView View { get; }

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
