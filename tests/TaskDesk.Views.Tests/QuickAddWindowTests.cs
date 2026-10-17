using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using Windows.System;

namespace TaskDesk.Views.Tests;

/// <summary>The quick-add card the global hotkey opens: Enter adds the Task, Escape or losing focus dismisses it.</summary>
[TestClass]
public sealed class QuickAddWindowTests
{
    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task Enter_adds_the_Task_to_the_Default_List_and_closes_the_card()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var (window, viewModel) = await OpenAsync(db, ct);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        var created = 0;
        viewModel.TaskCreated += () => created++;
        try
        {
            VisualTree.Find<TextBox>(window.View, "TitleBox").Text = "Book flights";

            Assert.IsTrue(window.View.HandleKey(VirtualKey.Enter), "Enter is handled by the card.");

            await UiWait.UntilAsync(() => closed, () => "Enter did not close the card.", ct);
            Assert.AreEqual(1, created);
            var inbox = (await db.QueryAsync(c => c.Lists)).Single();
            var tasks = await db.QueryAsync(c => c.Tasks);
            Assert.AreEqual("Book flights", tasks.Single().Title);
            Assert.AreEqual(inbox.Id, tasks.Single().ListId);
        }
        finally
        {
            if (!closed)
            {
                window.Close();
            }
        }
    }

    [UITestMethod]
    public async Task Escape_dismisses_the_card_without_adding_a_Task()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var (window, _) = await OpenAsync(db, ct);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            VisualTree.Find<TextBox>(window.View, "TitleBox").Text = "Not this one";

            Assert.IsTrue(window.View.HandleKey(VirtualKey.Escape), "Escape is handled by the card.");

            await UiWait.UntilAsync(() => closed, () => "Escape did not close the card.", ct);
            Assert.AreEqual(0, (await db.QueryAsync(c => c.Tasks)).Count);
        }
        finally
        {
            if (!closed)
            {
                window.Close();
            }
        }
    }

    [UITestMethod]
    public async Task Enter_with_an_empty_title_keeps_the_card_open()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var (window, _) = await OpenAsync(db, ct);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            window.View.HandleKey(VirtualKey.Enter);
            await Task.Delay(200, ct);

            Assert.IsFalse(closed, "An empty title must not close the card.");
            Assert.AreEqual(0, (await db.QueryAsync(c => c.Tasks)).Count);
        }
        finally
        {
            if (!closed)
            {
                window.Close();
            }
        }
    }

    [UITestMethod]
    public async Task Other_keys_are_left_to_the_focused_control()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var (window, _) = await OpenAsync(db, ct);
        try
        {
            Assert.IsFalse(window.View.HandleKey(VirtualKey.A));
            Assert.IsFalse(window.View.HandleKey(VirtualKey.Tab));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_card_is_borderless_always_on_top_fixed_size_hidden_from_switchers_and_titled_for_UI_tests()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var (window, _) = await OpenAsync(db, ct);
        try
        {
            Assert.AreEqual("TaskDesk quick add", window.Title);
            var presenter = (OverlappedPresenter)window.AppWindow.Presenter;
            Assert.IsTrue(presenter.IsAlwaysOnTop, "always on top");
            Assert.IsFalse(presenter.IsResizable, "not resizable");
            Assert.IsFalse(presenter.HasBorder, "no system border");
            Assert.IsFalse(presenter.HasTitleBar, "no title bar");
            Assert.IsFalse(window.AppWindow.IsShownInSwitchers, "hidden from the taskbar and the switchers");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_title_box_has_focus_and_the_fields_keep_their_accessible_names()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var (window, viewModel) = await OpenAsync(db, ct);
        try
        {
            var title = VisualTree.Find<TextBox>(window.View, "TitleBox");
            await UiWait.UntilAsync(
                () => title.XamlRoot is { } root && ReferenceEquals(FocusManager.GetFocusedElement(root), title),
                () => "The title box did not take keyboard focus.", ct);
            Assert.AreEqual("Task title", AutomationProperties.GetName(title));
            var list = VisualTree.Find<ComboBox>(window.View, "ListBox");
            Assert.AreEqual("List", AutomationProperties.GetName(list));
            Assert.AreSame(viewModel.SelectedList, list.SelectedItem, "The Default List is preselected.");
            Assert.AreEqual("Inbox", ((TaskList)list.SelectedItem).Name);
            Assert.AreEqual("Due date", AutomationProperties.GetName(VisualTree.Find<CalendarDatePicker>(window.View, "DueBox")));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_picked_Due_date_is_saved_with_the_Task()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var (window, _) = await OpenAsync(db, ct);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            VisualTree.Find<TextBox>(window.View, "TitleBox").Text = "Pay rent";
            VisualTree.Find<CalendarDatePicker>(window.View, "DueBox").Date = new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Local));

            window.View.HandleKey(VirtualKey.Enter);

            await UiWait.UntilAsync(() => closed, () => "Enter did not close the card.", ct);
            Assert.AreEqual(new DateOnly(2026, 11, 1), (await db.QueryAsync(c => c.Tasks)).Single().DueDate);
        }
        finally
        {
            if (!closed)
            {
                window.Close();
            }
        }
    }

    [UITestMethod]
    public async Task Losing_activation_dismisses_the_card()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var (window, _) = await OpenAsync(db, ct);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        var other = new Window();
        try
        {
            await UiWait.UntilAsync(() => window.HasBeenActivated, () => "The card was never activated.", ct);
            other.Activate();

            await UiWait.UntilAsync(() => closed, () => "Activating another window did not dismiss the card.", ct);
        }
        finally
        {
            other.Close();
            if (!closed)
            {
                window.Close();
            }
        }
    }

    [UITestMethod]
    public async Task The_card_follows_the_theme_it_is_registered_for()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var (window, _) = await OpenAsync(db, ct);
        try
        {
            var themes = new ThemeService();
            themes.Register(window);
            var card = VisualTree.Find<Border>(window.View, "Card");
            Windows.UI.Color Background() => ((SolidColorBrush)card.Background).Color;

            themes.Apply(ThemeChoice.Light);
            await UiWait.UntilAsync(() => window.View.ActualTheme == ElementTheme.Light, () => "Light did not apply.", ct);
            var light = Background();
            themes.Apply(ThemeChoice.Dark);
            await UiWait.UntilAsync(() => window.View.ActualTheme == ElementTheme.Dark, () => "Dark did not apply.", ct);
            await UiWait.UntilAsync(() => Background() != light,
                () => $"The card background stayed {light} in Dark.", ct);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task<(QuickAddWindow Window, QuickAddViewModel ViewModel)> OpenAsync(
        TempDatabase db, CancellationToken ct)
    {
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var viewModel = new QuickAddViewModel(db.Get<ITaskCreator>(), db.Get<IListService>());
        await viewModel.LoadAsync(ct);
        var window = new QuickAddWindow(viewModel);
        window.Activate();
        await UiWait.UntilAsync(() => window.View.IsLoaded,
            () => "The card did not open.", ct);
        return (window, viewModel);
    }
}
