using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using Windows.UI;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The command-driven toggles of the main window (<see cref="CommandToggleButton"/> and
/// <see cref="CommandCheckBox"/>): a screen reader's Toggle runs their Command once without flipping them, and the
/// text inside a pressed one takes Fluent's checked foreground.
/// </summary>
[TestClass]
public sealed class CommandToggleTests
{
    // Every command-driven toggle of the main window with the Filter bar shown, one Tag, and the detail pane open
    // on a Task with one Subtask, by the name a screen reader reads.
    private static readonly string[] MainWindowToggles =
        ["#work", "Completed", "Completed", "Filter", "Has due filter", "Has reminder filter", "Subtask done"];

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task Main_window_command_driven_toggles_are_all_found()
    {
        using var db = await DatabaseAsync();
        var (window, _) = await OpenWithEveryToggleAsync(db, ElementTheme.Default);
        try
        {
            CollectionAssert.AreEqual(MainWindowToggles, CommandToggles(window).Select(Describe).Order().ToList());
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Screen_reader_toggle_runs_the_command_of_every_command_driven_toggle_once_without_flipping_it()
    {
        using var db = await DatabaseAsync();
        var (window, _) = await OpenWithEveryToggleAsync(db, ElementTheme.Default);
        try
        {
            var toggles = CommandToggles(window);
            Assert.IsNotEmpty(toggles, "The main window has no command-driven toggle.");
            foreach (var button in toggles)
            {
                var label = Describe(button);
                var original = button.Command;
                Assert.IsNotNull(original, $"'{label}' is a command-driven toggle without a Command.");
                var command = new RecordingCommand();
                button.Command = command;
                try
                {
                    var before = button.IsChecked;

                    ((ToggleButtonAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(button)).Toggle();

                    Assert.HasCount(1, command.Executions,
                        $"Toggling '{label}' ran its command {command.Executions.Count} times.");
                    Assert.AreEqual(button.CommandParameter, command.Executions[0],
                        $"Toggling '{label}' ran its command with another parameter.");
                    // The command did not change the bound state, so the toggle must not look changed either.
                    Assert.AreEqual(before, button.IsChecked, $"Toggling '{label}' flipped it without its command.");
                }
                finally
                {
                    button.Command = original;
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    [DataRow(ElementTheme.Light)]
    [DataRow(ElementTheme.Dark)]
    public async Task Text_inside_a_pressed_command_driven_toggle_takes_Fluents_checked_foreground(ElementTheme theme)
    {
        // Fluent pairs its checked foreground with the accent fill it gives a pressed toggle, so text drawn in it reads.
        var ink = (await ThemeColours.ResolveAsync(theme, ["ToggleButtonForegroundChecked"], Ct))["ToggleButtonForegroundChecked"];
        using var db = await DatabaseAsync();
        var (window, viewModel) = await OpenWithEveryToggleAsync(db, theme);
        try
        {
            // Press the Filter button (the Filter bar shows), Has due and the #work chip.
            await viewModel.ToggleHasDueFilterCommand.ExecuteAsync(null);
            await viewModel.ToggleFilterTagCommand.ExecuteAsync(viewModel.FilterTagChips.Single().Tag);
            List<ToggleButton> Pressed() =>
                [.. CommandToggles(window)
                    .Where(b => b is CommandToggleButton && b.IsChecked == true)];
            await UiWait.UntilAsync(() => Pressed().Count == 3,
                () => $"Pressed: [{string.Join(", ", Pressed().Select(Describe))}].", Ct);

            foreach (var button in Pressed())
            {
                var label = Describe(button);
                // The text and icon the button draws, as each one's colour.
                List<(string Text, Color Colour)> Drawn() =>
                [
                    .. VisualTree.Descendants(button).OfType<TextBlock>()
                        .Select(t => (t.Text, Colour(t.Foreground))),
                    .. VisualTree.Descendants(button).OfType<FontIcon>()
                        .Select(i => ($"icon {i.Glyph}", Colour(i.Foreground))),
                ];
                await UiWait.UntilAsync(() => Drawn().Count > 0 && Drawn().All(d => d.Colour == ink),
                    () => $"{theme}: in pressed '{label}', " + string.Join(", ", Drawn().Select(d => $"'{d.Text}' is {d.Colour}"))
                        + $", not Fluent's checked foreground {ink}.", Ct);
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static Color Colour(Brush brush) => ((SolidColorBrush)brush).Color;

    // The command-driven toggles shown in the window; a container kept for recycling holds no item and is skipped.
    private static List<ToggleButton> CommandToggles(MainWindow window) =>
        [.. VisualTree.Descendants(window.Content).OfType<ToggleButton>()
            .Where(b => b is CommandToggleButton or CommandCheckBox)
            .Where(b => b.XamlRoot is not null && !InRecycledContainer(b))];

    private static bool InRecycledContainer(DependencyObject element)
    {
        for (var node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ListViewItem container && ItemsControl.ItemsControlFromItemContainer(container) is ListView list)
            {
                return list.IndexFromContainer(container) < 0;
            }
        }

        return false;
    }

    // The name a screen reader reads: the accessible name, else the content.
    private static string Describe(ToggleButton button) =>
        FrameworkElementAutomationPeer.CreatePeerForElement(button).GetName();

    // The main window at a width that docks the detail pane, with the Filter bar shown, one Tag chip, one Task row
    // and the detail pane open on that Task with one Subtask.
    private async Task<(MainWindow Window, MainViewModel ViewModel)> OpenWithEveryToggleAsync(
        TempDatabase db, ElementTheme theme)
    {
        var inbox = (await db.QueryAsync(c => c.Lists)).Single().Id;
        await db.AddTaskAsync(inbox, "Pack for the trip", completed: false);
        var taskId = (await db.QueryAsync(c => c.Tasks)).Single().Id;
        await db.Get<ITagService>().AddTagToTaskAsync(taskId, "work", Ct);
        await db.Get<ITaskService>().AddSubtaskAsync(taskId, "Passport", Ct);
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock,
            dueService: db.Get<IDueService>(), searchService: db.Get<ITaskSearchService>(),
            listService: db.Get<IListService>());
        var window = new MainWindow(viewModel);
        try
        {
            ((FrameworkElement)window.Content).RequestedTheme = theme;
            window.Activate();
            var content = (FrameworkElement)window.Content;
            await UiWait.UntilAsync(() => content.IsLoaded, () => "The window did not load.", Ct);
            var scale = content.XamlRoot.RasterizationScale;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(
                (int)Math.Round(1280 * scale), (int)Math.Round(800 * scale)));
            await UiWait.UntilAsync(() => Math.Abs(content.ActualWidth - 1280) < 1,
                () => $"The content is {content.ActualWidth} px wide, not 1280.", Ct);
            await viewModel.LoadAsync(Ct);
            viewModel.ToggleFilterBarCommand.Execute(null);
            viewModel.OpenDetailPane(viewModel.Tasks.Single());
            await UiWait.UntilAsync(() => CommandToggles(window).Count == MainWindowToggles.Length,
                () => $"Toggles shown: [{string.Join(", ", CommandToggles(window).Select(Describe).Order())}].", Ct);
            return (window, viewModel);
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    private async Task<TempDatabase> DatabaseAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return db;
    }
}
