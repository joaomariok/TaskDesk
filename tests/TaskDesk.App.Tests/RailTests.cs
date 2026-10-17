using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TaskDesk.App.Styles;
using TaskDesk.App.Views;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using static TaskDesk.App.Tests.ElementTree;

namespace TaskDesk.App.Tests;

/// <summary>
/// In the narrow rail every List shows its initial inside a ring of its colour, so Lists without a colour no
/// longer look alike, and the count badges read at body-text contrast in Light and Dark.
/// </summary>
[Collection(WpfAppCollection.Name)]
public sealed class RailTests(WpfAppFixture fixture)
{
    private static readonly Size LayoutSize = new(56, 700);

    [Fact]
    public void Each_rail_List_shows_its_initial_in_a_ring_of_its_colour() => fixture.Run(() =>
    {
        var lists = new ListsViewModel(Stub.Of<IListService>(), Stub.Of<IListDialogs>());
        lists.Lists.Add(NewList("Work", null));
        lists.Lists.Add(NewList("Groceries", null));
        lists.Lists.Add(NewList("Home", "#D13438"));
        var pane = RailWith(lists);

        var rail = (ListBox)pane.FindName("RailListsBox");
        var items = lists.Lists.Select(l => (ListBoxItem)rail.ItemContainerGenerator.ContainerFromItem(l)).ToList();

        Assert.Equal(["W", "G", "H"], items.Select(i => Descendants(i).OfType<TextBlock>().Single().Text));
        Assert.Equal(Color.FromRgb(0xD1, 0x34, 0x38), RingColour(items[2]));
        Assert.Equal(((SolidColorBrush)pane.FindResource("ListStripeBrush")).Color, RingColour(items[0]));
    });

    [Theory]
    [InlineData("Light", "RailBadge", "RailBadgeText")]
    [InlineData("Dark", "RailBadge", "RailBadgeText")]
    [InlineData("Light", "RailBadgeOverdue", "RailBadgeOverdueText")]
    [InlineData("Dark", "RailBadgeOverdue", "RailBadgeOverdueText")]
    public void Rail_badge_text_reads_at_body_text_contrast(string theme, string badgeStyle, string textStyle) =>
        fixture.Run(() =>
    {
        var colours = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Resources/Theme/{theme}.xaml"),
        };
        var badge = (Style)fixture.App.FindResource(badgeStyle);
        var text = (Style)fixture.App.FindResource(textStyle);

        var window = ColourOf(colours, "SolidBackgroundFillColorBase");
        var fill = Over(ColourOf(colours, KeyOf(badge, Border.BackgroundProperty)), window);
        var ink = Over(ColourOf(colours, KeyOf(text, TextBlock.ForegroundProperty)), fill);

        Assert.True(Contrast(ink, fill) >= 4.5, $"{theme}: badge text contrast is {Contrast(ink, fill):0.00}:1");
    });

    private static NavigationPane RailWith(ListsViewModel lists)
    {
        var main = new MainViewModel(Stub.Of<ITaskService>(), Stub.Of<ITagService>());
        var pane = new NavigationPane { DataContext = main };
        pane.Initialize(main, lists);
        pane.SetRail(true);
        pane.ApplyTemplate();
        pane.Measure(LayoutSize);
        pane.Arrange(new Rect(LayoutSize));
        pane.UpdateLayout();
        return pane;
    }

    private static Color RingColour(DependencyObject item) =>
        ((SolidColorBrush)Descendants(item).OfType<Ellipse>().Single().Stroke).Color;

    private static ListItemViewModel NewList(string name, string? colour) =>
        new(new ListSummary(new TaskList { Id = Guid.NewGuid(), Name = name, ColorHex = colour }, 0));

    // The resource key of a {DynamicResource} setter, followed through BasedOn.
    private static string KeyOf(Style style, DependencyProperty property) =>
        style.Setters.OfType<Setter>().Where(s => s.Property == property)
            .Select(s => (string)((DynamicResourceExtension)s.Value).ResourceKey).FirstOrDefault()
        ?? KeyOf(style.BasedOn!, property);

    // A Fluent brush key ("...Brush"), colour key or meaning-named brush, read from one theme's colour dictionary.
    private static Color ColourOf(ResourceDictionary colours, string key) =>
        colours[key = FoundationResources.FluentKeyOf(key)] switch
        {
            SolidColorBrush brush => brush.Color,
            Color colour => colour,
            _ => (Color)colours[key.EndsWith("Brush", StringComparison.Ordinal) ? key[..^5] : key],
        };

    private static Color Over(Color top, Color below)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a));
        return Color.FromRgb(Mix(top.R, below.R), Mix(top.G, below.G), Mix(top.B, below.B));
    }

    private static double Contrast(Color x, Color y)
    {
        double Luminance(Color c)
        {
            double Channel(byte v) => v / 255.0 <= 0.04045 ? v / 255.0 / 12.92 : Math.Pow((v / 255.0 + 0.055) / 1.055, 2.4);
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        var (light, dark) = (Math.Max(Luminance(x), Luminance(y)), Math.Min(Luminance(x), Luminance(y)));
        return (light + 0.05) / (dark + 0.05);
    }
}
