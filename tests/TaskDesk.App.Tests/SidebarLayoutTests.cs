using System.Windows;
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
/// The laid-out sidebar shows only counts that tell the owner something, and holds Lists, "+ New List" and
/// Tags in one scroll area under the fixed Smart Views.
/// </summary>
[Collection(WpfAppCollection.Name)]
public sealed class SidebarLayoutTests(WpfAppFixture fixture)
{
    private static readonly Size LayoutSize = new(260, 700);

    [Fact]
    public void A_count_of_zero_is_collapsed_and_a_count_above_zero_is_visible() => fixture.Run(() =>
    {
        WithSidebar(
            (main, lists) =>
            {
                main.TodayCount = 0;
                main.UpcomingCount = 3;
                main.AllCount = 12;
                lists.Lists.Add(NewList("Groceries", 0));
                lists.Lists.Add(NewList("Work", 5));
            },
            pane =>
            {
                var counts = CountsIn(pane, "SmartViewList");
                Assert.Equal(["0", "12", "3"], counts.Select(c => c.Text).Order());
                Assert.All(counts, c => Assert.Equal(c.Text == "0" ? Visibility.Collapsed : Visibility.Visible, c.Visibility));

                var listCounts = CountsIn(pane, "ListsBox");
                Assert.Equal(["0", "5"], listCounts.Select(c => c.Text).Order());
                Assert.All(listCounts, c => Assert.Equal(c.Text == "0" ? Visibility.Collapsed : Visibility.Visible, c.Visibility));
            });
    });

    [Fact]
    public void A_count_that_rises_above_zero_becomes_visible_again() => fixture.Run(() =>
    {
        WithSidebar(
            (main, lists) =>
            {
                main.TodayCount = 0;
                lists.Lists.Add(NewList("Work", 0));
            },
            (pane, main, lists) =>
            {
                main.TodayCount = 2;
                lists.Lists[0].OpenTaskCount = 4;
                pane.UpdateLayout();

                Assert.All(CountsIn(pane, "SmartViewList").Where(c => c.Text == "2"), c => Assert.Equal(Visibility.Visible, c.Visibility));
                Assert.Equal(Visibility.Visible, Assert.Single(CountsIn(pane, "ListsBox")).Visibility);
            });
    });

    [Fact]
    public void Lists_new_list_and_tags_share_one_scroll_area_in_that_order() => fixture.Run(() =>
    {
        WithSidebar(
            (main, lists) =>
            {
                lists.Lists.Add(NewList("Inbox", 1));
                lists.Lists.Add(NewList("Work", 2));
                main.Tags.Add(new Tag { Id = Guid.NewGuid(), Name = "home" });
            },
            pane =>
            {
                var scroll = Assert.IsType<ScrollViewer>(pane.FindName("SidebarScroll"));
                var smartViews = (ListBox)pane.FindName("SmartViewList");
                var listsBox = (ListBox)pane.FindName("ListsBox");
                var newList = (Button)pane.FindName("NewListButton");
                var tags = (ListBox)pane.FindName("TagList");

                Assert.False(IsInside(smartViews, scroll), "The Smart Views must stay fixed above the scroll area.");
                Assert.True(IsInside(listsBox, scroll), "The Lists are not in the scroll area.");
                Assert.True(IsInside(newList, scroll), "'+ New List' is not in the scroll area.");
                Assert.True(IsInside(tags, scroll), "The Tags are not in the scroll area.");

                var listsTop = TopOf(listsBox, pane);
                var newListTop = TopOf(newList, pane);
                var tagsTop = TopOf(tags, pane);
                Assert.True(listsTop < newListTop && newListTop < tagsTop,
                    $"Expected Lists ({listsTop}) < '+ New List' ({newListTop}) < Tags ({tagsTop}).");

                // Directly under the last List: no band of empty space between them.
                Assert.InRange(newListTop - (listsTop + listsBox.ActualHeight), 0, 16);

                // The scroll area is the only scroller: the Lists and Tags never scroll on their own.
                Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.GetVerticalScrollBarVisibility(listsBox));
                Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.GetVerticalScrollBarVisibility(tags));
            });
    });

    [Fact]
    public void Tag_names_line_up_with_List_names_behind_a_hash_icon() => fixture.Run(() =>
    {
        WithSidebar(
            (main, lists) =>
            {
                lists.Lists.Add(NewList("Work", 2));
                main.Tags.Add(new Tag { Id = Guid.NewGuid(), Name = "home" });
            },
            pane =>
            {
                var listName = TextIn(pane, "ListsBox", "Work");
                var tagName = TextIn(pane, "TagList", "home");
                var hash = TextIn(pane, "TagList", "#");

                Assert.Equal(LeftOf(listName, pane), LeftOf(tagName, pane));
                Assert.True(LeftOf(hash, pane) < LeftOf(tagName, pane), "The '#' must sit in the icon column, left of the name.");
                Assert.Equal(TextFillColor(pane, "TextFillColorSecondaryBrush"), ((SolidColorBrush)hash.Foreground).Color);
            });
    });

    [Fact]
    public void The_Tags_section_is_hidden_while_there_are_no_Tags() => fixture.Run(() =>
    {
        WithSidebar(
            (_, lists) => lists.Lists.Add(NewList("Inbox", 0)),
            pane =>
            {
                var tags = (ListBox)pane.FindName("TagList");
                Assert.False(tags.IsVisible || (tags.Parent as UIElement)?.IsVisible == true);
            });
    });

    private static ListItemViewModel NewList(string name, int openTasks) =>
        new(new ListSummary(new TaskList { Id = Guid.NewGuid(), Name = name }, openTasks));

    private void WithSidebar(Action<MainViewModel, ListsViewModel> arrange, Action<NavigationPane> assert) =>
        WithSidebar(arrange, (pane, _, _) => assert(pane));

    private void WithSidebar(
        Action<MainViewModel, ListsViewModel> arrange, Action<NavigationPane, MainViewModel, ListsViewModel> assert)
    {
        var main = new MainViewModel(Stub.Of<ITaskService>(), Stub.Of<ITagService>());
        var lists = new ListsViewModel(Stub.Of<IListService>(), Stub.Of<IListDialogs>());
        arrange(main, lists);
        var pane = new NavigationPane { DataContext = main };
        pane.Initialize(main, lists);
        pane.ApplyTemplate();
        pane.Measure(LayoutSize);
        pane.Arrange(new Rect(LayoutSize));
        pane.UpdateLayout();
        assert(pane, main, lists);
    }

    // The count text blocks inside the named list (SidebarCount is the soft-ink count at the end of a row).
    private static List<TextBlock> CountsIn(NavigationPane pane, string listName)
    {
        var style = (Style)pane.FindResource("SidebarCount");
        return [.. Descendants((DependencyObject)pane.FindName(listName)).OfType<TextBlock>().Where(t => t.Style == style)];
    }

    private static bool IsInside(DependencyObject node, DependencyObject ancestor)
    {
        for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private static TextBlock TextIn(FrameworkElement pane, string listName, string text) =>
        Descendants((DependencyObject)pane.FindName(listName)).OfType<TextBlock>().Single(t => t.Text == text);

    private static double LeftOf(UIElement element, UIElement relativeTo) =>
        element.TranslatePoint(new Point(0, 0), relativeTo).X;

    private static Color TextFillColor(FrameworkElement element, string key) =>
        ((SolidColorBrush)element.FindResource(key)).Color;

    private static double TopOf(UIElement element, UIElement relativeTo) =>
        element.TranslatePoint(new Point(0, 0), relativeTo).Y;
}
