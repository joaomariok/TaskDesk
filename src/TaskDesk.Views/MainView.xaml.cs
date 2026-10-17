using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskDesk.Presentation;

namespace TaskDesk.Views;

/// <summary>The main window's content: quick add and the Task list area of the page the view model shows.</summary>
public sealed partial class MainView : UserControl
{
    public MainView(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        TaskList = new TaskListView(viewModel);
        Grid.SetRow(TaskList, 1);
        RootGrid.Children.Add(TaskList);
    }

    /// <summary>The Overdue, open and "Completed (N)" Task lists and the Undo bar.</summary>
    public TaskListView TaskList { get; }

    public MainViewModel ViewModel { get; }

    /// <summary>Gives quick add keyboard focus, as the window does at start-up.</summary>
    public void FocusQuickAdd() => QuickAddBox.Focus(FocusState.Programmatic);
}
