using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskDesk.App.Views;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;

namespace TaskDesk.App.Tests;

/// <summary>
/// How Task rows look once laid out: completed rows are dimmed and struck through except on the Completed
/// Smart View, and the Overdue and "Completed (N)" Task lists draw_no_border_or_background, like the open Task list.
/// </summary>
[Collection(WpfAppCollection.Name)]
public sealed class TaskRowLookTests(WpfAppFixture fixture)
{
    private static readonly Size LayoutSize = new(600, 100);

    [Theory]
    [InlineData(false, 0.6, true)]
    [InlineData(true, 1.0, false)]
    public void Completed_row_is_dimmed_and_struck_through_unless_it_keeps_full_strength(
        bool keepsFullStrength, double opacity, bool struck) => fixture.Run(() =>
    {
        var area = new TaskListArea();
        var template = (DataTemplate)area.Resources["TaskRowTemplate"];
        var item = new TaskItem { Id = Guid.NewGuid(), Title = "Done", IsCompleted = true };
        var presenter = new ContentPresenter
        {
            Content = new TaskRowViewModel(item, keepsFullStrength: keepsFullStrength),
            ContentTemplate = template,
        };
        presenter.Measure(LayoutSize);
        presenter.Arrange(new Rect(LayoutSize));
        presenter.UpdateLayout();

        var root = (FrameworkElement)template.FindName("RowRoot", presenter);
        var title = (TextBlock)template.FindName("RowTitle", presenter);

        Assert.Equal(opacity, root.Opacity);
        Assert.Equal(struck, title.TextDecorations.Contains(TextDecorations.Strikethrough[0]));
    });

    [Theory]
    [InlineData("OverdueList")]
    [InlineData("CompletedList")]
    [InlineData("OpenList")]
    public void Task_lists_draw_no_border(string name) => fixture.Run(() =>
    {
        var area = new TaskListArea();
        var list = (ListBox)area.FindName(name);
        // Laid out, so Fluent's ListBox style has applied and a border it draws would show.
        area.Measure(new Size(1280, 800));
        area.Arrange(new Rect(0, 0, 1280, 800));
        area.UpdateLayout();
        list.ApplyTemplate();

        Assert.Equal(new Thickness(0), list.BorderThickness);
        Assert.Equal(Colors.Transparent, ((SolidColorBrush)list.Background).Color);
    });
}
