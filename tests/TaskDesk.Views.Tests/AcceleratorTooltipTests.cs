using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The WPF app shows no tooltip for its shortcuts. WinUI shows an element's keyboard accelerators (F2, Ctrl+Z,
/// Ctrl+F, Ctrl+N, Ctrl+1..5) in a tooltip over that element unless its KeyboardAcceleratorPlacementMode is Hidden;
/// over a whole pane that tooltip reads as a stray "F2" floating over the window.
/// </summary>
[TestClass]
public sealed class AcceleratorTooltipTests
{
    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task No_element_of_the_main_window_shows_its_keyboard_accelerators_in_a_tooltip()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, listService: db.Get<IListService>());

        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await viewModel.LoadAsync(ct);
            var root = (UIElement)window.Content;
            // Every shortcut the main window wires as an accelerator, so the walk below covers them all.
            string[] expected = ["F2", "Control+Z", "Control+F", "Control+N", "Control+Number1", "Control+Number5"];
            List<UIElement> owners = [];
            HashSet<string> keys = [];
            await UiWait.UntilAsync(
                () =>
                {
                    owners = [.. VisualTree.Descendants(root).Prepend(root).OfType<UIElement>()
                        .Where(e => e.KeyboardAccelerators.Count > 0)];
                    keys = [.. owners.SelectMany(e => e.KeyboardAccelerators).Select(Describe)];
                    return expected.All(keys.Contains);
                },
                () => $"Accelerators found: {string.Join(", ", keys)}", ct);

            // Only an element on screen can show a tooltip: NavigationView's own Back button (Windows+Back) is in
            // the tree but collapsed, as the window has no Back button.
            var shown = owners.Where(e => e.ActualSize.X > 0 && e.ActualSize.Y > 0)
                .Where(e => e.KeyboardAcceleratorPlacementMode != KeyboardAcceleratorPlacementMode.Hidden)
                .Select(e => $"{e.GetType().Name} ({string.Join(", ", e.KeyboardAccelerators.Select(Describe))})")
                .ToList();
            Assert.IsEmpty(shown, "These show their accelerators in a tooltip: " + string.Join("; ", shown));
        }
        finally
        {
            window.Close();
        }
    }

    private static string Describe(KeyboardAccelerator accelerator) =>
        accelerator.Modifiers == Windows.System.VirtualKeyModifiers.None
            ? accelerator.Key.ToString()
            : $"{accelerator.Modifiers}+{accelerator.Key}";
}
