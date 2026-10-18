using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using TaskDesk.Core.Shell;
using TaskDesk.Presentation;
using Windows.Graphics;
using Windows.System;

namespace TaskDesk.Views;

/// <summary>
/// The main window. It only hosts <see cref="MainView"/>; the shell owns its lifecycle (show, hide to the
/// tray, saved size and position) through <see cref="Window.AppWindow"/>.
/// </summary>
public sealed partial class MainWindow : Window
{
    // The window's size and minimum size in DIPs, as the WPF window had them; the shell restores a saved layout
    // over the default.
    public const double DefaultWidth = 900;
    public const double DefaultHeight = 600;
    public const double MinimumWidth = 800;
    public const double MinimumHeight = 500;

    private bool _closed;

    public MainWindow(MainViewModel viewModel, ListsViewModel? lists = null)
    {
        InitializeComponent();
        SetDefaultSize();
        View = new MainView(viewModel);
        Lists = lists;
        View.Lists = lists;
        // The page with the detail pane beside it or over it.
        Details = new DetailDock(View);
        // The sidebar's NavigationView hosts the page as its content. Deleting a Tag asks on this window.
        Navigation = new NavigationPane(viewModel, new Dialogs.DialogService(() => this), lists) { PageContent = Details };
        if (lists is not null)
        {
#pragma warning disable VSTHRD101 // Event handlers; a failed reload reaches the app's UnhandledException log
            // Open-Task counts change when Tasks are added, completed, un-completed, deleted or restored.
            viewModel.OpenTaskCountsChanged += async (_, _) => await lists.LoadAsync();
            // A renamed, recoloured or deleted List: reload so the heading and the rows show its new name and
            // colour, and a page on a deleted List falls back to the Default List.
            lists.ListEdited += async (_, _) => await viewModel.LoadAsync();
#pragma warning restore VSTHRD101
        }

        Content = Navigation;
        // The window's width decides the rail and whether the detail pane docks.
        FollowWindowWidth();
        AppWindow.Changed += (_, e) =>
        {
            if (e.DidSizeChange && !_closed)
            {
                FollowWindowWidth();
            }
        };
        Closed += (_, _) =>
        {
            // A closed window has no handle or size to measure.
            _closed = true;
            // Stops the view models' deferred work now: each stop latches and cancels before it returns, so a text
            // change delivered after Closed or a tick already queued starts nothing, and a search or load still running
            // stops before the caller lets the database go. Not awaited: the cancelled work ends on its own, quietly.
            _ = StopViewModelsAsync();
        };
        // Ctrl+comma opens Settings from anywhere in the window, before a text box sees the key.
        Navigation.PreviewKeyDown += OnPreviewKeyDown;
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

    /// <summary>
    /// Call when the exit starts, before its flush and host stop: the window stays open until the exit has run, so
    /// its Closed comes too late. The sidebar, page and detail pane take no more input, so nothing there starts a
    /// reload, and both view models stop (<see cref="MainViewModel.StopAsync"/>, <see cref="ListsViewModel.StopAsync"/>);
    /// completes once the work they cancelled has ended.
    /// </summary>
    public Task PrepareForExitAsync()
    {
        Navigation.IsEnabled = false;
        return StopViewModelsAsync();
    }

    // The page's view model and the sidebar's Lists; both stops are idempotent.
    private Task StopViewModelsAsync() => Task.WhenAll(ViewModel.StopAsync(), Lists?.StopAsync() ?? Task.CompletedTask);

    // Opens at the default size (the shell then restores a saved layout), never smaller than the minimum.
    private void SetDefaultSize()
    {
        var scale = Scale();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = WindowLayoutCalculator.ToPhysical(MinimumWidth, scale);
            presenter.PreferredMinimumHeight = WindowLayoutCalculator.ToPhysical(MinimumHeight, scale);
        }

        AppWindow.Resize(new SizeInt32(
            WindowLayoutCalculator.ToPhysical(DefaultWidth, scale), WindowLayoutCalculator.ToPhysical(DefaultHeight, scale)));
    }

    /// <summary>
    /// The window's width in device-independent pixels, its frame included (what the WPF window's width
    /// measured): the sidebar is the rail below <see cref="PaneLayout.RailBelowWidth"/> and the detail pane
    /// docks above <see cref="PaneLayout.DetailDockedAboveWidth"/>.
    /// </summary>
    public double WindowWidth => AppWindow.Size.Width / Scale();

    // A minimized window keeps the layout it had.
    private void FollowWindowWidth()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            return;
        }

        var width = WindowWidth;
        Navigation.SetRail(PaneLayout.IsRail(width));
        ViewModel.IsDetailDocked = PaneLayout.IsDetailDocked(width);
    }

    // 1.0 at 100 % display scale, 1.25 at 125 %.
    private double Scale() => WindowScale.Of(this);

    /// <summary>
    /// Runs the window-wide shortcut for a key: Ctrl+comma opens Settings. Returns false when the key is not it.
    /// A keyboard accelerator would not do: one for the comma key (VK_OEM_COMMA) was never invoked on a real key press.
    /// </summary>
    public bool TryShortcut(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        if ((key, modifiers) != (SettingsKey, VirtualKeyModifiers.Control))
        {
            return false;
        }

        ViewModel.OpenSettingsCommand.Execute(null);
        return true;
    }

    /// <summary>VK_OEM_COMMA: <see cref="VirtualKey"/> has no name for it.</summary>
    public const VirtualKey SettingsKey = (VirtualKey)0xBC;

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == SettingsKey && TryShortcut(e.Key, KeyboardModifiers.Current()))
        {
            e.Handled = true;
        }
    }
}
