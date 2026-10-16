using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;

namespace TaskDesk.App;

public partial class MainWindow : Window
{
    private const string DragFormat = "TaskDesk.TaskItem";
    private Point _dragStart;
    private Point _listDragStart;
    private bool _syncingTagSelection;

    public MainWindow(MainViewModel viewModel, ListsViewModel lists)
    {
        ViewModel = viewModel;
        Lists = lists;
        DataContext = viewModel;
        InitializeComponent();
        PriorityBox.ItemsSource = Enum.GetValues<Priority>();

        // Open-Task counts change when Tasks are added, completed, un-completed, deleted or restored.
        viewModel.OpenTaskCountsChanged += async (_, _) => await lists.LoadAsync();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTask))
            {
                SyncTaskListSelection();
            }
            else if (e.PropertyName == nameof(MainViewModel.CurrentView) && viewModel.CurrentView is not null)
            {
                // A Smart View replaces the Tag filter.
                _syncingTagSelection = true;
                TagList.SelectedItem = null;
                _syncingTagSelection = false;
            }
        };
    }

    public MainViewModel ViewModel { get; }
    public ListsViewModel Lists { get; }

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

    // F2 edits the title of the selected Task in the detail pane.
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && ViewModel.SelectedTask is not null)
        {
            ViewModel.IsDetailPaneOpen = true;
            DetailTitleBox.Focus();
            DetailTitleBox.SelectAll();
            e.Handled = true;
        }
    }

    private async void OnTagSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingTagSelection)
        {
            return;
        }

        // Reloading replaces the Tags collection, which clears the selection; keep it in sync afterwards.
        if (TagList.SelectedItem is Tag tag)
        {
            await ViewModel.FilterByTagAsync(tag);
            _syncingTagSelection = true;
            TagList.SelectedItem = ViewModel.TagFilter;
            _syncingTagSelection = false;
        }
    }

    private async void OnShowAllClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.FilterByTagAsync(null);
        _syncingTagSelection = true;
        TagList.SelectedItem = null;
        _syncingTagSelection = false;
    }

    private async void OnDeleteTagClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TagFilter is not { } tag)
        {
            MessageBox.Show(this, "Select a tag in the list first.", "Delete tag");
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Delete the tag #{tag.Name}? It is removed from all tasks. This cannot be undone.",
            "Delete tag",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            await ViewModel.DeleteTagAsync(tag);
        }
    }

    // The context menu belongs to a list row; its placement target carries the row's view model.
    private static ListItemViewModel? ItemOf(object sender)
        => ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget is FrameworkElement target
            ? target.DataContext as ListItemViewModel
            : null;

    // The Default List cannot be deleted, so its Delete entry is disabled.
    private void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget: FrameworkElement { DataContext: ListItemViewModel item } } menu)
        {
            foreach (var entry in menu.Items.OfType<MenuItem>().Where(m => m.Tag is "delete"))
            {
                entry.IsEnabled = !item.IsDefault;
            }
        }
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            await Lists.RenameCommand.ExecuteAsync(item);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            await Lists.DeleteCommand.ExecuteAsync(item);
        }
    }

    private async void OnColorClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item && sender is MenuItem { Tag: string hex })
        {
            await Lists.SetColorAsync(item, hex);
        }
    }

    private void OnListMouseDown(object sender, MouseButtonEventArgs e) => _listDragStart = e.GetPosition(null);

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not ListBox box)
        {
            return;
        }

        var delta = e.GetPosition(null) - _listDragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (box.SelectedItem is ListItemViewModel item)
        {
            DragDrop.DoDragDrop(box, item, DragDropEffects.Move);
        }
    }

    private async void OnListDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ListItemViewModel)) is ListItemViewModel dragged
            && (e.OriginalSource as FrameworkElement)?.DataContext is ListItemViewModel target)
        {
            await Lists.MoveAsync(dragged, target);
        }
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
