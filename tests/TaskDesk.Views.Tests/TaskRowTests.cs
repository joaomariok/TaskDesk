using Microsoft.Extensions.Time.Testing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;

namespace TaskDesk.Views.Tests;

/// <summary>
/// A Task row read at a glance: the Completed checkbox, the List colour stripe, the title, the Due chip and the Tag
/// chips. The row is shown alone in a live window, as a Task list shows it.
/// </summary>
[TestClass]
public sealed class TaskRowTests
{
    // Friday 16 October 2026, 09:30 UTC; the fake clock's zone is UTC.
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.Date);

    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task A_row_shows_the_Completed_checkbox_the_List_stripe_and_the_title()
    {
        var list = new TaskList { Id = Guid.NewGuid(), Name = "Home", ColorHex = "#107C10" };
        var row = Row(new TaskItem { Id = Guid.NewGuid(), Title = "Water plants", ListId = list.Id }, list);

        var view = new TaskRow { Row = row };
        using var host = await Hosted.ShowAsync(view, TestContext.CancellationToken);

        var check = VisualTree.Descendants(view).OfType<CheckBox>().Single();
        Assert.AreEqual("Completed", AutomationProperties.GetName(check));
        Assert.IsFalse(check.IsChecked);
        var stripe = VisualTree.Find<Border>(view, "ListStripe");
        Assert.AreEqual(Windows.UI.Color.FromArgb(255, 0x10, 0x7C, 0x10), ((SolidColorBrush)stripe.Background).Color);
        Assert.AreEqual(1.0, stripe.Opacity);
        Assert.AreEqual("Water plants", VisualTree.Find<TextBlock>(view, "RowTitle").Text);
    }

    [UITestMethod]
    public async Task The_row_checkbox_is_Fluents_box_with_4px_corners_whole_and_without_label_room()
    {
        var view = new TaskRow { Row = Row(new TaskItem { Id = Guid.NewGuid(), Title = "Call the bank" }) };
        using var host = await Hosted.ShowAsync(view, TestContext.CancellationToken);

        var check = VisualTree.Descendants(view).OfType<CheckBox>().Single();
        // Fluent's template: its 20 px box is the rectangle named NormalRectangle.
        var box = VisualTree.Find<Microsoft.UI.Xaml.Shapes.Rectangle>(check, "NormalRectangle");
        Assert.AreEqual(20, box.ActualWidth);
        Assert.AreEqual(20, box.ActualHeight);
        // As in the WPF app (DESIGN.md "Task Checkbox"): Fluent's square box with its 4 px corners, not a circle.
        // The template binds the radius to the CheckBox's CornerRadius; that binding settles after the first layout.
        await UiWait.UntilAsync(() => box.RadiusX == 4 && box.RadiusY == 4,
            () => $"The box's corners are {box.RadiusX} x {box.RadiusY} px, not Fluent's 4 px.", TestContext.CancellationToken);
        // The whole box lies inside the checkbox (the older CheckBox template cut it).
        var at = box.TransformToVisual(check).TransformPoint(default);
        Assert.IsTrue(at.Y >= 0 && at.Y + box.ActualHeight <= check.ActualHeight,
            $"The box spans {at.Y} to {at.Y + box.ActualHeight} px in a {check.ActualHeight} px checkbox.");
        // Fluent's CheckBox otherwise reserves 120 px of width for a label.
        Assert.AreEqual(20, check.ActualWidth, "The checkbox keeps room for a label.");
    }

    [UITestMethod]
    public async Task A_screen_reader_Toggle_on_the_row_checkbox_runs_its_command_once_and_leaves_the_box_to_the_Task()
    {
        var row = Row(new TaskItem { Id = Guid.NewGuid(), Title = "Water plants" });
        var command = new RecordingCommand();
        var view = new TaskRow { Row = row, ToggleCompleteCommand = command };
        using var host = await Hosted.ShowAsync(view, TestContext.CancellationToken);
        var check = VisualTree.Descendants(view).OfType<CheckBox>().Single();

        var peer = (Microsoft.UI.Xaml.Automation.Peers.ToggleButtonAutomationPeer)
            Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(check);
        Assert.AreEqual(Microsoft.UI.Xaml.Automation.ToggleState.Off, peer.ToggleState);
        peer.Toggle();

        Assert.HasCount(1, command.Executions, "The Toggle did not run the command exactly once.");
        Assert.AreSame(row, command.Executions[0]);
        // The command did not complete the Task, so the box must not look ticked.
        Assert.IsFalse(check.IsChecked);
    }

    [UITestMethod]
    [DataRow(-1, "Yesterday", "OverdueBrush", DisplayName = "Overdue")]
    [DataRow(0, "Today", "TodayBrush", DisplayName = "Today")]
    [DataRow(1, "Tomorrow", "TextFillColorSecondaryBrush", DisplayName = "Later")]
    public async Task The_Due_chip_reads_the_Due_and_takes_the_colour_of_its_state(
        int days, string text, string brushKey)
    {
        var ct = TestContext.CancellationToken;
        var view = new TaskRow
        {
            RequestedTheme = ElementTheme.Light,
            Row = Row(new TaskItem { Id = Guid.NewGuid(), Title = "Pay rent", DueDate = Today.AddDays(days) }),
        };
        using var host = await Hosted.ShowAsync(view, ct);
        var colours = await ThemeColours.ResolveAsync(ElementTheme.Light, [brushKey], ct);

        var chip = VisualTree.Find<Border>(view, "DueChip");
        var chipText = VisualTree.Find<TextBlock>(view, "DueText");
        Assert.AreEqual(Visibility.Visible, chip.Visibility);
        Assert.AreEqual(text, chipText.Text);
        Assert.AreEqual("Due", AutomationProperties.GetName(chipText));
        Assert.AreEqual(colours[brushKey], ((SolidColorBrush)chipText.Foreground).Color, "Due text colour");
        if (brushKey != "TextFillColorSecondaryBrush")
        {
            Assert.AreEqual(colours[brushKey], ((SolidColorBrush)chip.BorderBrush).Color, "Due chip outline colour");
        }
    }

    [UITestMethod]
    public async Task A_Task_without_a_Due_shows_no_Due_chip()
    {
        var view = new TaskRow { Row = Row(new TaskItem { Id = Guid.NewGuid(), Title = "Someday" }) };
        using var host = await Hosted.ShowAsync(view, TestContext.CancellationToken);

        Assert.AreEqual(Visibility.Collapsed, VisualTree.Find<Border>(view, "DueChip").Visibility);
    }

    [UITestMethod]
    [DataRow(false, 0.6, true, DisplayName = "Dimmed and struck through")]
    [DataRow(true, 1.0, false, DisplayName = "Full strength on the Completed Smart View")]
    public async Task A_completed_row_is_dimmed_and_struck_through_unless_it_keeps_full_strength(
        bool keepsFullStrength, double opacity, bool struck)
    {
        var view = new TaskRow
        {
            Row = Row(new TaskItem { Id = Guid.NewGuid(), Title = "Done", IsCompleted = true },
                keepsFullStrength: keepsFullStrength),
        };
        using var host = await Hosted.ShowAsync(view, TestContext.CancellationToken);

        Assert.AreEqual(opacity, VisualTree.Find<Grid>(view, "RowRoot").Opacity, 0.001);
        Assert.AreEqual(struck, VisualTree.Find<TextBlock>(view, "RowTitle").TextDecorations
            == Windows.UI.Text.TextDecorations.Strikethrough);
        Assert.IsTrue(VisualTree.Descendants(view).OfType<CheckBox>().Single().IsChecked);
    }

    [UITestMethod]
    public async Task A_row_Tag_chip_reads_hash_and_name_and_Tags_beyond_two_read_as_more()
    {
        var view = new TaskRow
        {
            Row = Row(new TaskItem
            {
                Id = Guid.NewGuid(),
                Title = "Book dentist",
                Tags = [NewTag("health"), NewTag("errands"), NewTag("city")],
            }),
        };
        using var host = await Hosted.ShowAsync(view, TestContext.CancellationToken);

        var tags = VisualTree.Descendants(view).OfType<ItemsControl>()
            .Single(c => AutomationProperties.GetName(c) == "Tags");
        CollectionAssert.AreEqual(new[] { "#health", "#errands" }, Texts(tags));
        var more = VisualTree.Descendants(view).OfType<TextBlock>().Single(t => AutomationProperties.GetName(t) == "More tags");
        Assert.AreEqual("+1", more.Text);
        Assert.AreEqual(Visibility.Visible, more.Visibility);
    }

    [UITestMethod]
    public async Task A_row_shows_the_List_caption_Priority_flag_Subtask_progress_and_Repeats_icon_when_they_apply()
    {
        var list = new TaskList { Id = Guid.NewGuid(), Name = "Work" };
        var item = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = "Send report",
            ListId = list.Id,
            Priority = Priority.High,
            Recurrence = Recurrence.Daily(),
            Subtasks = [new Subtask { Id = Guid.NewGuid(), Title = "Draft", IsCompleted = true }, new Subtask { Id = Guid.NewGuid(), Title = "Send" }],
        };
        var view = new TaskRow { Row = Row(item, list, showListName: true) };
        using var host = await Hosted.ShowAsync(view, TestContext.CancellationToken);

        Assert.AreEqual("Work", Shown("List").Text);
        Assert.AreEqual("1/2", Shown("Subtask progress").Text);
        Shown("High priority");
        Shown("Repeats");

        TextBlock Shown(string name)
        {
            var element = VisualTree.Descendants(view).OfType<TextBlock>().Single(t => AutomationProperties.GetName(t) == name);
            Assert.AreEqual(Visibility.Visible, element.Visibility, $"'{name}' is hidden.");
            return element;
        }
    }

    [UITestMethod]
    public async Task A_plain_row_hides_the_List_caption_Priority_flag_Subtask_progress_Repeats_icon_and_Tags()
    {
        var view = new TaskRow { Row = Row(new TaskItem { Id = Guid.NewGuid(), Title = "Plain" }) };
        using var host = await Hosted.ShowAsync(view, TestContext.CancellationToken);

        // The checkbox draws its glyph with a TextBlock of its own; only the row's own text counts here.
        var inCheckBox = VisualTree.Descendants(VisualTree.Descendants(view).OfType<CheckBox>().Single()).ToHashSet();
        var shown = VisualTree.Descendants(view).OfType<TextBlock>()
            .Where(t => !inCheckBox.Contains(t))
            .Where(t => t.Text.Length > 0 && t.ActualWidth > 0 && t.Name != "RowTitle")
            .Select(t => AutomationProperties.GetName(t) + ":" + t.Text);
        Assert.AreEqual(0, shown.Count(), "Shown: " + string.Join(", ", shown));
    }

    private static Tag NewTag(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private static List<string> Texts(DependencyObject root) =>
        [.. VisualTree.Descendants(root).OfType<TextBlock>().Select(t => t.Text)];

    private static TaskRowViewModel Row(
        TaskItem item, TaskList? list = null, bool showListName = false, bool keepsFullStrength = false) =>
        new(item, new FakeTimeProvider(Now), list, showListName, keepsFullStrength);
}
