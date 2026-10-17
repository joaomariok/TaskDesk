using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskDesk.Core.Tags;
using TaskDesk.Presentation;

namespace TaskDesk.App.Views;

/// <summary>The sidebar: Smart Views, Lists (rename, color, delete, drag to reorder) and Tags.</summary>
public partial class NavigationPane : UserControl
{
    private Point _listDragStart;
    private bool _syncingTagSelection;

    public NavigationPane()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel { get; set; } = null!;
    private ListsViewModel Lists { get; set; } = null!;

    /// <summary>Connects the pane to the view models; called once by <see cref="MainWindow"/>.</summary>
    public void Initialize(MainViewModel viewModel, ListsViewModel lists)
    {
        ViewModel = viewModel;
        Lists = lists;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentView) && viewModel.CurrentView is not null)
            {
                // A Smart View replaces the Tag filter.
                _syncingTagSelection = true;
                TagList.SelectedItem = null;
                _syncingTagSelection = false;
            }
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnShowAllClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.FilterByTagAsync(null);
        _syncingTagSelection = true;
        TagList.SelectedItem = null;
        _syncingTagSelection = false;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnDeleteTagClick(object sender, RoutedEventArgs e)
    {
        // The message boxes stay owned by the main window, as before the pane was split out.
        var owner = Window.GetWindow(this);
        if (ViewModel.TagFilter is not { } tag)
        {
            MessageBox.Show(owner, "Select a tag in the list first.", "Delete tag");
            return;
        }

        var answer = MessageBox.Show(
            owner,
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            await Lists.RenameCommand.ExecuteAsync(item);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            await Lists.DeleteCommand.ExecuteAsync(item);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF event handler; the signature requires void.")]

    private async void OnListDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ListItemViewModel)) is ListItemViewModel dragged
            && (e.OriginalSource as FrameworkElement)?.DataContext is ListItemViewModel target)
        {
            await Lists.MoveAsync(dragged, target);
        }
    }
}
