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
    private bool _syncingTagSelection;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

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

        if (ItemUnder(e.OriginalSource) is { DataContext: TaskItem task } item)
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
        if (!ViewModel.IsReorderEnabled || e.Data.GetData(DragFormat) is not TaskItem dragged)
        {
            return;
        }

        var oldIndex = ViewModel.Tasks.IndexOf(dragged);
        var newIndex = ItemUnder(e.OriginalSource)?.DataContext is TaskItem over
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
