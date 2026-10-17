using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TaskDesk.App.Views;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;

namespace TaskDesk.App.Tests;

/// <summary>
/// A Tag reads "#" plus its name wherever it is shown: on a row, in the Filter row and in the detail pane,
/// where only the chip's own remove button removes it.
/// </summary>
[Collection(WpfAppCollection.Name)]
public sealed class TagChipTests(WpfAppFixture fixture)
{
    private static readonly Size LayoutSize = new(1280, 800);

    [Fact]
    public void Row_tag_chip_reads_hash_and_name() => fixture.Run(() =>
    {
        var area = new TaskListArea();
        var template = (DataTemplate)area.Resources["TaskRowTemplate"];
        var item = new TaskItem { Id = Guid.NewGuid(), Title = "Book dentist", Tags = [NewTag("health")] };
        var presenter = new ContentPresenter { Content = new TaskRowViewModel(item), ContentTemplate = template };
        presenter.Measure(new Size(600, 100));
        presenter.Arrange(new Rect(0, 0, 600, 100));
        presenter.UpdateLayout();

        var tags = Descendants(presenter).OfType<ItemsControl>().Single(c => AutomationProperties.GetName(c) == "Tags");

        Assert.Equal(["#health"], Texts(tags));
    });

    [Fact]
    public void Filter_row_tag_toggle_reads_hash_and_name() => fixture.Run(() =>
    {
        var viewModel = new MainViewModel(Stub.Of<ITaskService>(), Stub.Of<ITagService>()) { IsFilterBarOpen = true };
        viewModel.FilterTagChips.Add(new FilterTagChip(NewTag("work"), false));
        var area = new TaskListArea { DataContext = viewModel };
        LayOut(area);

        var toggle = Descendants(area).OfType<ToggleButton>().Single(b => b.CommandParameter is Tag);

        Assert.Equal(["#work"], Texts(toggle));
    });

    [Fact]
    public void Detail_pane_tag_chip_reads_hash_and_name_and_only_its_button_removes_it() => fixture.Run(() =>
    {
        var health = NewTag("health");
        var viewModel = new MainViewModel(Stub.Of<ITaskService>(), Stub.Of<ITagService>());
        var window = new MainWindow(viewModel, new ListsViewModel(Stub.Of<IListService>(), Stub.Of<IListDialogs>()));
        try
        {
            viewModel.OpenDetailPane(
                new TaskRowViewModel(new TaskItem { Id = Guid.NewGuid(), Title = "Book dentist", Tags = [health] }));
            window.ApplyTemplate();
            var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
            LayOut(root);

            var tags = Descendants(root).OfType<ItemsControl>().Single(c => AutomationProperties.GetName(c) == "Task tags");
            var chip = Assert.Single(Descendants(tags).OfType<ContentPresenter>(), p => p.Content == health);
            var remove = Assert.Single(Descendants(chip).OfType<ButtonBase>());

            Assert.Equal(["#health"], Texts(chip).Where(t => t.Length > 1));
            Assert.Equal("Remove #health", AutomationProperties.GetName(remove));
            Assert.Equal("Remove tag", remove.ToolTip);
            Assert.Same(viewModel.RemoveTagCommand, remove.Command);
            Assert.Same(health, remove.CommandParameter);
        }
        finally
        {
            window.Close();
        }
    });

    private static Tag NewTag(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private static void LayOut(FrameworkElement element)
    {
        element.Measure(LayoutSize);
        element.Arrange(new Rect(LayoutSize));
        element.UpdateLayout();
    }

    private static List<string> Texts(DependencyObject root) =>
        [.. Descendants(root).OfType<TextBlock>().Where(t => t.IsVisible || t.Visibility == Visibility.Visible)
            .Select(t => t.Text).Where(t => t.Length > 0)];

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
