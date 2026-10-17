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

    [Fact]
    public void Task_rows_draw_Fluents_list_item() => fixture.Run(() =>
    {
        var area = new TaskListArea();
        var row = new ListBoxItem { Style = (Style)area.Resources["TaskRowContainer"], Content = "Call the bank" };
        var plain = new ListBoxItem { Content = "Call the bank" };
        var host = new StackPanel { Children = { row, plain } };
        host.Measure(LayoutSize);
        host.Arrange(new Rect(LayoutSize));

        // A plain ListBoxItem takes Fluent's implicit style, so its template is Fluent's.
        Assert.Same(plain.Template, row.Template);
    });

    [Fact]
    public void Task_rows_stay_one_compact_line() => fixture.Run(() =>
    {
        var area = new TaskListArea();
        var item = new TaskItem { Id = Guid.NewGuid(), Title = "Call the bank" };
        var row = new ListBoxItem
        {
            Style = (Style)area.Resources["TaskRowContainer"],
            Content = new TaskRowViewModel(item),
            ContentTemplate = (DataTemplate)area.Resources["TaskRowTemplate"],
        };
        var host = new StackPanel { Children = { row } };
        host.Measure(LayoutSize);
        host.Arrange(new Rect(LayoutSize));

        // The 22 px checkbox, 1 px of padding and the item's 1 px border above and below: Fluent's own list
        // item padding (12 px all round) would make every row 44 px tall.
        Assert.Equal(26, row.ActualHeight);
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
