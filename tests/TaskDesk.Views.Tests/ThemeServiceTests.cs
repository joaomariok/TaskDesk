using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using static TaskDesk.Views.Tests.ThemeColours;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The theme setting applied live: the theme service switches every open window, its content and its title bar,
/// and System follows Windows.
/// </summary>
[TestClass]
public sealed class ThemeServiceTests
{
    // Fluent's primary text colour, which the quick-add box draws its text in.
    private static readonly Windows.UI.Color LightText = Rgb(0xE4000000);
    private static readonly Windows.UI.Color DarkText = Colors.White;

    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task A_switch_changes_an_open_windows_resolved_brushes_and_title_bar()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var window = await OpenMainWindowAsync(db, ct);
        try
        {
            var themes = new ThemeService();
            themes.Register(window);
            var quickAdd = VisualTree.Find<TextBox>(window.View, "QuickAddBox");
            Windows.UI.Color Text() => ((SolidColorBrush)quickAdd.Foreground).Color;

            themes.Apply(ThemeChoice.Light);
            await UiWait.UntilAsync(() => Text() == LightText, () => $"Light: quick add text is {Text()}.", ct);
            Assert.AreEqual(TitleBarTheme.Light, window.AppWindow.TitleBar.PreferredTheme);

            themes.Apply(ThemeChoice.Dark);
            await UiWait.UntilAsync(() => Text() == DarkText, () => $"Dark: quick add text is {Text()}.", ct);
            Assert.AreEqual(TitleBarTheme.Dark, window.AppWindow.TitleBar.PreferredTheme);
            Assert.AreEqual(ElementTheme.Dark, window.View.ActualTheme);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task System_follows_the_Windows_app_mode()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var window = await OpenMainWindowAsync(db, ct);
        try
        {
            var themes = new ThemeService();
            themes.Register(window);
            themes.Apply(ThemeChoice.Dark);

            themes.Apply(ThemeChoice.System);

            Assert.AreEqual(ElementTheme.Default, ((FrameworkElement)window.Content).RequestedTheme);
            Assert.AreEqual(
                Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light,
                window.View.ActualTheme);
            Assert.AreEqual(TitleBarTheme.UseDefaultAppMode, window.AppWindow.TitleBar.PreferredTheme);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_window_opened_after_a_switch_takes_the_chosen_theme()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var themes = new ThemeService();
        themes.Apply(ThemeChoice.Dark);
        var window = await OpenMainWindowAsync(db, ct);
        try
        {
            themes.Register(window);

            Assert.AreEqual(ElementTheme.Dark, window.View.ActualTheme);
            Assert.AreEqual(TitleBarTheme.Dark, window.AppWindow.TitleBar.PreferredTheme);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_closed_window_is_left_alone_by_later_switches()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var themes = new ThemeService();
        var closed = await OpenMainWindowAsync(db, ct);
        themes.Register(closed);
        closed.Close();

        themes.Apply(ThemeChoice.Light);
        themes.Apply(ThemeChoice.Dark);

        var open = await OpenMainWindowAsync(db, ct);
        try
        {
            themes.Register(open);
            Assert.AreEqual(ElementTheme.Dark, open.View.ActualTheme);
        }
        finally
        {
            open.Close();
        }
    }

    private static async Task<MainWindow> OpenMainWindowAsync(TempDatabase db, CancellationToken ct)
    {
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, listService: db.Get<IListService>());
        var window = new MainWindow(viewModel);
        window.Activate();
        await UiWait.UntilAsync(() => window.View.IsLoaded, () => "The main window never loaded.", ct);
        return window;
    }
}
