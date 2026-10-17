using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using TaskDesk.App.Controls;
using TaskDesk.App.Views;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using static TaskDesk.App.Tests.ElementTree;

namespace TaskDesk.App.Tests;

/// <summary>
/// The command-driven CheckBox and ToggleButton draw Fluent's own templates, and a Task row's checkbox is
/// Fluent's box without the room Fluent reserves for a label, so the row stays one compact line. A style
/// set on either control, also inside a template, wins over Fluent's.
/// </summary>
[Collection(WpfAppCollection.Name)]
public sealed class CommandControlStyleTests(WpfAppFixture fixture)
{
    private static readonly Size LayoutSize = new(600, 100);

    [Fact]
    public void Task_row_checkbox_is_Fluents_box_without_label_room() => fixture.Run(() =>
    {
        var area = new TaskListArea();
        var template = (DataTemplate)area.Resources["TaskRowTemplate"];
        var item = new TaskItem { Id = Guid.NewGuid(), Title = "Call the bank" };
        var presenter = new ContentPresenter { Content = new TaskRowViewModel(item), ContentTemplate = template };
        presenter.Measure(LayoutSize);
        presenter.Arrange(new Rect(LayoutSize));
        presenter.UpdateLayout();

        var checkBox = Descendants(presenter).OfType<CommandCheckBox>().Single();

        Assert.Same(TemplateOf((Style)fixture.App.FindResource(typeof(CheckBox))), checkBox.Template);
        // Fluent's 20 px box inside its 1 px root border; Fluent's CheckBox otherwise reserves 32 px of height
        // and 120 px of width for a label.
        Assert.Equal(new Size(22, 22), checkBox.RenderSize);
    });

    [Theory]
    [InlineData(typeof(CommandCheckBox))]
    [InlineData(typeof(CommandToggleButton))]
    public void Style_set_in_a_template_wins(Type control) => fixture.Run(() =>
    {
        var style = new Style(control.BaseType!, (Style)fixture.App.FindResource(control.BaseType!));
        var template = (DataTemplate)XamlReader.Parse(
            $$"""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:c="clr-namespace:TaskDesk.App.Controls;assembly=TaskDesk.App">
                <c:{{control.Name}} Style="{DynamicResource Marker}" />
            </DataTemplate>
            """);
        var presenter = new ContentPresenter { Content = new object(), ContentTemplate = template };
        presenter.Resources["Marker"] = style;
        presenter.Measure(LayoutSize);
        presenter.Arrange(new Rect(LayoutSize));

        var button = Descendants(presenter).OfType<ToggleButton>().Single();

        Assert.Same(style, button.Style);
    });

    [Theory]
    [InlineData(typeof(CommandCheckBox))]
    [InlineData(typeof(CommandToggleButton))]
    public void Without_a_style_it_draws_Fluents_template(Type control) => fixture.Run(() =>
    {
        var button = (ToggleButton)Activator.CreateInstance(control)!;
        var host = new Border { Child = button };
        host.Measure(LayoutSize);
        host.Arrange(new Rect(LayoutSize));

        var fluent = (Style)fixture.App.FindResource(control.BaseType!);

        Assert.Same(TemplateOf(fluent), button.Template);
    });

    private static ControlTemplate TemplateOf(Style style) =>
        style.Setters.OfType<Setter>().Where(s => s.Property == Control.TemplateProperty)
            .Select(s => (ControlTemplate)s.Value).FirstOrDefault() ?? TemplateOf(style.BasedOn!);
}
