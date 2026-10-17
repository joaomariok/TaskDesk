using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TaskDesk.Presentation;

namespace TaskDesk.Views;

/// <summary>The main window's content: quick add and the Task rows of the page the view model shows.</summary>
public sealed partial class MainView : UserControl
{
    public MainView(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    /// <summary>Gives quick add keyboard focus, as the window does at start-up.</summary>
    public void FocusQuickAdd() => QuickAddBox.Focus(FocusState.Programmatic);

    // UI Automation names a row by its Task title (the UI tests find rows by name).
    private void OnRowContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is TaskRowViewModel row)
        {
            AutomationProperties.SetName(args.ItemContainer, row.Title);
        }
    }
}
