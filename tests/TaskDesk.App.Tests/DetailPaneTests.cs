using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using TaskDesk.App.Views;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using static TaskDesk.App.Tests.ElementTree;

namespace TaskDesk.App.Tests;

/// <summary>
/// The detail pane shows a validation error right under its field and any other error under the fixed header,
/// shows the time format as a hint inside the empty time boxes, shows the List as text rather than as a
/// read-only box, and parts the fixed header from the scrolling fields with a hairline.
/// </summary>
[Collection(WpfAppCollection.Name)]
public sealed class DetailPaneTests(WpfAppFixture fixture)
{
    private static readonly Size LayoutSize = new(1280, 1400);

    [Fact]
    public void A_Due_time_error_shows_between_the_Due_field_and_the_Reminder_label() => WithPane((viewModel, root) =>
    {
        viewModel.DetailDueTime = "abc";
        Relayout();

        var error = ShownText(root, "Enter the Due time as HH:mm, for example 14:30.");

        Assert.InRange(Top(error, root), Bottom(Named<DatePicker>(root, "Due date"), root),
            Top(Label(root, "Reminder"), root));
    });

    [Fact]
    public void A_Reminder_time_error_shows_between_the_Reminder_field_and_the_Priority_label() =>
        WithPane((viewModel, root) =>
        {
            viewModel.DetailReminderTime = "noon";
            Relayout();

            var error = ShownText(root, "Enter the Reminder time as HH:mm, for example 14:30.");

            Assert.InRange(Top(error, root), Bottom(Named<DatePicker>(root, "Reminder date"), root),
                Top(Label(root, "Priority"), root));
        });

    [Fact]
    public void A_general_error_shows_under_the_header_above_the_fields() => WithPane((viewModel, root) =>
    {
        viewModel.ErrorMessage = "Could not save the changes: disk full";
        Relayout();

        var error = ShownText(root, "Could not save the changes: disk full");

        Assert.True(Top(error, root) < Top(Label(root, "Subtasks"), root));
        Assert.True(Top(error, root) >= Bottom(Named<TextBox>(root, "Task title"), root));
    });

    [Theory]
    [InlineData("Due time", "HH:mm")]
    [InlineData("Reminder time", "09:00")]
    public void An_empty_time_box_shows_its_format_as_a_hint(string box, string hint) => WithPane((viewModel, root) =>
    {
        var textBox = Named<TextBox>(root, box);

        Assert.Equal(Visibility.Visible, Hint(textBox, hint).Visibility);
        Assert.Contains(hint, AutomationProperties.GetHelpText(textBox));

        textBox.Text = "14:30";
        Relayout();

        Assert.Equal(Visibility.Collapsed, Hint(textBox, hint).Visibility);
    });

    [Fact]
    public void The_List_shows_as_text_not_as_a_box() => WithPane((_, root) =>
    {
        var list = Named<FrameworkElement>(root, "List");

        Assert.IsNotType<TextBox>(list);
        Assert.Contains("Work", Descendants(list).OfType<TextBlock>().Select(t => t.Text));
        Assert.False(list.Focusable);
    });

    [Fact]
    public void A_hairline_parts_the_fixed_header_from_the_scrolling_fields() => WithPane((_, root) =>
    {
        var scroller = Descendants(root).OfType<ScrollViewer>().First(s => Grid.GetRow(s) == 1);
        var header = ((Grid)scroller.Parent).Children.OfType<Border>().Single(b => Grid.GetRow(b) == 0);

        Assert.Equal(new Thickness(0, 0, 0, 1), header.BorderThickness);
        Assert.NotNull(header.BorderBrush);
    });

    private void WithPane(Action<MainViewModel, FrameworkElement> test) => fixture.Run(() =>
    {
        var viewModel = new MainViewModel(Stub.Of<ITaskService>(), Stub.Of<ITagService>());
        var window = new MainWindow(viewModel, new ListsViewModel(Stub.Of<IListService>(), Stub.Of<IListDialogs>()));
        try
        {
            var list = new TaskList { Id = Guid.NewGuid(), Name = "Work" };
            viewModel.OpenDetailPane(new TaskRowViewModel(
                new TaskItem { Id = Guid.NewGuid(), Title = "Book dentist", DueDate = new DateOnly(2026, 10, 25) },
                list: list));
            window.ApplyTemplate();
            _windowRoot = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
            LayOut(_windowRoot);
            test(viewModel, Descendants(_windowRoot).OfType<DetailPane>().Single());
        }
        finally
        {
            window.Close();
        }
    });

    private FrameworkElement? _windowRoot;

    // After a change, lays the whole window out again so positions are final.
    private void Relayout() => LayOut(_windowRoot!);

    private static void LayOut(FrameworkElement element)
    {
        element.Measure(LayoutSize);
        element.Arrange(new Rect(LayoutSize));
        element.UpdateLayout();
    }

    private static T Named<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants(root).OfType<T>().Single(e => AutomationProperties.GetName(e) == name);

    private static TextBlock Label(DependencyObject root, string text) =>
        Descendants(root).OfType<TextBlock>().Single(t => t.Text == text);

    // The one laid-out TextBlock reading text; a collapsed one has no size.
    private static TextBlock ShownText(DependencyObject root, string text) =>
        Assert.Single(Descendants(root).OfType<TextBlock>(), t => t.Text == text && t.ActualHeight > 0);

    // The hint drawn over a time box: a TextBlock in the same cell, out of hit testing.
    private static TextBlock Hint(TextBox box, string text) =>
        Assert.Single(((Panel)box.Parent).Children.OfType<TextBlock>(), t => t.Text == text && !t.IsHitTestVisible);

    private static double Top(FrameworkElement element, FrameworkElement root) =>
        element.TranslatePoint(new Point(0, 0), root).Y;

    private static double Bottom(FrameworkElement element, FrameworkElement root) =>
        Top(element, root) + element.ActualHeight;
}
