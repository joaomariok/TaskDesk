using Microsoft.UI.Xaml;
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
        Content = View;
        // The quick-add box has the initial focus of the window.
        View.Loaded += (_, _) => View.FocusQuickAdd();
    }

    public MainView View { get; }

    public MainViewModel ViewModel => View.ViewModel;
}
