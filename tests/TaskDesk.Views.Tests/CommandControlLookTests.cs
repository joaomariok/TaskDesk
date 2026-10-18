using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The command-driven controls look like the WPF app's. Every checkbox of the main window looks like the WPF app's: Fluent's 20 px box with its 4 px corners
/// (DESIGN.md "Task Checkbox"), drawn whole inside the checkbox. The command-driven check box once fell back to
/// the framework's older template, which puts the box 6 px down a 32 px cell (a 20 px check box cut it) and
/// ignores CornerRadius. Covers the row's box, the detail pane's Completed box, the Subtask boxes and the Repeat
/// editor's boxes. The command-driven toggle buttons (Filter and the filter bar) take Fluent's 4 px corners too.
/// </summary>
[TestClass]
public sealed class CommandControlLookTests
{
    private const double BoxSize = 20;
    private const double BoxCorner = 4;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task Every_checkbox_draws_Fluents_square_box_with_4px_corners_whole_inside_the_checkbox()
    {
        using var db = new TempDatabase();
        db.Clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        var id = await AddAsync(db, "Water plants", new DateOnly(2026, 10, 19)); // a Monday
        await db.Get<ITaskService>().AddSubtaskAsync(id, "Fill the can", Ct);
        await db.Get<IRecurrenceService>().SetRecurrenceAsync(id, Recurrence.Weekly(RecurrenceDays.Monday, 1), Ct);
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock,
            dueService: db.Get<IDueService>(), recurrenceService: db.Get<IRecurrenceService>(),
            listService: db.Get<IListService>());

        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await UiWait.UntilAsync(() => window.Content is FrameworkElement { IsLoaded: true },
                () => "The window did not load.", Ct);
            var content = (FrameworkElement)window.Content;
            var scale = content.XamlRoot.RasterizationScale;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(
                (int)Math.Round(1200 * scale), (int)Math.Round(1000 * scale)));
            await viewModel.LoadAsync(Ct);
            var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
            await UiWait.UntilAsync(
                () => VisualTree.Descendants(list).OfType<ListViewItem>().Any(i => AutomationProperties.GetName(i) == "Water plants"),
                () => "No row for the Task.", Ct);
            list.SelectedItem = viewModel.Tasks.Single();
            await UiWait.UntilAsync(() => Shown(content).Count(c => AutomationProperties.GetName(c) == "Monday") == 1,
                () => "The detail pane shows no weekday boxes.", Ct);

            var boxes = Shown(content);
            CollectionAssert.IsSubsetOf(
                new[] { "Completed", "Subtask done", "Monday", "Sunday", "Repeat from the completion date" },
                boxes.Select(AutomationProperties.GetName).ToList());
            Assert.AreEqual(2, boxes.Count(c => AutomationProperties.GetName(c) == "Completed"), "Row and pane boxes.");

            // The radius is bound to the checkbox's CornerRadius and settles after a layout pass.
            await UiWait.UntilAsync(() => boxes.All(c => Box(c).RadiusX == BoxCorner && Box(c).RadiusY == BoxCorner),
                () => "Not Fluent's 4 px corners: " + string.Join(", ",
                    boxes.Select(c => $"{AutomationProperties.GetName(c)} {Box(c).RadiusX}x{Box(c).RadiusY}")), Ct);
            foreach (var check in boxes)
            {
                var name = AutomationProperties.GetName(check);
                var box = Box(check);
                Assert.AreEqual(BoxSize, box.ActualWidth, $"{name}: box width");
                Assert.AreEqual(BoxSize, box.ActualHeight, $"{name}: box height");
                var at = box.TransformToVisual(check).TransformPoint(default);
                Assert.IsTrue(
                    at.X >= 0 && at.Y >= 0 && at.X + box.ActualWidth <= check.ActualWidth + 0.01
                        && at.Y + box.ActualHeight <= check.ActualHeight + 0.01,
                    $"{name}: the box ({at.X},{at.Y} {box.ActualWidth}x{box.ActualHeight}) runs past the checkbox " +
                    $"({check.ActualWidth}x{check.ActualHeight}), which cuts it.");
            }

            // The boxes without a label drop the 120 px Fluent reserves for one.
            foreach (var check in boxes.Where(c => c.Content is null))
            {
                Assert.AreEqual(BoxSize, check.ActualWidth, $"{AutomationProperties.GetName(check)}: width");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_command_toggle_button_draws_Fluents_ToggleButton_with_its_4px_corners()
    {
        var plain = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton { Content = "Has due" };
        var command = new CommandToggleButton { Content = "Has due" };
        var panel = new StackPanel { Children = { plain, command } };
        using var host = await Hosted.ShowAsync(panel, Ct);

        Assert.AreEqual(new CornerRadius(4), command.CornerRadius, "Not Fluent's 4 px corners.");
        Assert.AreSame(plain.Template, command.Template, "Not Fluent's ToggleButton template.");
        Assert.AreEqual(plain.ActualHeight, command.ActualHeight, "Not as tall as a Fluent toggle button.");
    }

    // Checkboxes on screen; a recycled row container is off screen.
    private static List<CheckBox> Shown(UIElement root) =>
        [.. VisualTree.Descendants(root).OfType<CheckBox>().Where(c => c.ActualWidth > 0 && c.ActualHeight > 0)];

    // Fluent's template: its 20 px box is the rectangle named NormalRectangle.
    private static Rectangle Box(CheckBox check) => VisualTree.Find<Rectangle>(check, "NormalRectangle");

    private static async Task<Guid> AddAsync(TempDatabase db, string title, DateOnly due)
    {
        var listId = (await db.QueryAsync(c => c.Lists.Where(l => l.IsDefault))).Single().Id;
        var now = db.Clock.GetUtcNow().UtcDateTime;
        var id = Guid.CreateVersion7();
        await using var context = await db.Get<Microsoft.EntityFrameworkCore.IDbContextFactory<TaskDeskDbContext>>()
            .CreateDbContextAsync();
        context.Tasks.Add(new TaskItem
        {
            Id = id,
            ListId = listId,
            Title = title,
            DueDate = due,
            CreatedUtc = now,
            UpdatedUtc = now,
        });
        await context.SaveChangesAsync();
        return id;
    }
}
