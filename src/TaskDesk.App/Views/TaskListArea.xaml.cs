using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;

namespace TaskDesk.App.Views;

/// <summary>
/// The Task list area: search and filters, quick add, the Overdue, open and Completed lists (with drag to
/// reorder), the Tags of the selected Task and the Undo bar.
/// </summary>
public partial class TaskListArea : UserControl
{
    private const string DragFormat = "TaskDesk.TaskItem";
    private Point _dragStart;

    public TaskListArea()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel { get; set; } = null!;

    /// <summary>Connects the area to the view model; called once by <see cref="MainWindow"/>.</summary>
    public void Initialize(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeSearch();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTask))
            {
                SyncTaskListSelection();
            }
        };
    }

    /// <summary>Focuses the search box with its text selected (Ctrl+F).</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // Only one of the three lists (overdue / open / completed) has a selection at a time.
    private void TaskList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Removals happen when a row moves between groups; the selection then follows the row.
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not TaskRowViewModel row)
        {
            return;
        }

        foreach (var other in new[] { OverdueList, OpenList, CompletedList })
        {
            if (!ReferenceEquals(sender, other))
            {
                other.SelectedItem = null;
            }
        }

        ViewModel.SelectedTask = row;
    }

    // Shows the view model's selection (for example after a reload) in whichever list holds the row.
    private void SyncTaskListSelection()
    {
        var row = ViewModel.SelectedTask;
        OverdueList.SelectedItem = row is not null && ViewModel.OverdueTasks.Contains(row) ? row : null;
        OpenList.SelectedItem = row is not null && ViewModel.Tasks.Contains(row) ? row : null;
        CompletedList.SelectedItem = row is not null && ViewModel.CompletedTasks.Contains(row) ? row : null;
    }

    private void TaskList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _dragStart = e.GetPosition(null);

    private void TaskList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !ViewModel.IsReorderEnabled)
        {
            return;
        }

        var offset = e.GetPosition(null) - _dragStart;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (ItemUnder(e.OriginalSource) is { DataContext: TaskRowViewModel task } item)
        {
            DragDrop.DoDragDrop(item, new DataObject(DragFormat, task), DragDropEffects.Move);
        }
    }

    private void TaskList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) && ViewModel.IsReorderEnabled
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void TaskList_Drop(object sender, DragEventArgs e)
    {
        if (!ViewModel.IsReorderEnabled || e.Data.GetData(DragFormat) is not TaskRowViewModel dragged)
        {
            return;
        }

        var oldIndex = ViewModel.Tasks.IndexOf(dragged);
        var newIndex = ItemUnder(e.OriginalSource)?.DataContext is TaskRowViewModel over
            ? ViewModel.Tasks.IndexOf(over)
            : ViewModel.Tasks.Count - 1; // dropped on empty space: move to the end

        await ViewModel.MoveTaskAsync(oldIndex, newIndex);
    }

    // ---- Search and filters ----

    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);
    private System.Windows.Threading.DispatcherTimer? _searchTimer;
    private bool _syncingPriorityFilter;

    private void InitializeSearch()
    {
        PriorityFilterBox.ItemsSource = new[] { "Any priority", "Low or higher", "Medium or higher", "High" };
        PriorityFilterBox.SelectedIndex = 0;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.MinPriorityFilter))
            {
                _syncingPriorityFilter = true;
                PriorityFilterBox.SelectedIndex = (int)(ViewModel.MinPriorityFilter ?? Priority.None);
                _syncingPriorityFilter = false;
            }
        };
    }

    // Reloads once typing pauses, so each keystroke does not query the database.
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = SearchDelay };
        _searchTimer.Tick -= OnSearchTimerTick;
        _searchTimer.Tick += OnSearchTimerTick;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void OnSearchTimerTick(object? sender, EventArgs e)
    {
        _searchTimer?.Stop();
        ViewModel.ApplySearchCommand.Execute(null);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void PriorityFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingPriorityFilter || PriorityFilterBox.SelectedIndex < 0)
        {
            return;
        }

        await ViewModel.SetMinPriorityFilterAsync((Priority)PriorityFilterBox.SelectedIndex);
    }

    private static ListBoxItem? ItemUnder(object source)
    {
        var element = source as DependencyObject;
        while (element is not null and not ListBoxItem)
        {
            element = VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element);
        }

        return element as ListBoxItem;
    }
}
