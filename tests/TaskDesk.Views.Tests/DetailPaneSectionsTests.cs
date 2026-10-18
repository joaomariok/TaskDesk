using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using TaskDesk.Views.Dialogs;
using Windows.Foundation;
using Windows.System;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The detail pane's Subtasks, Reminder, Repeat and Tags over a real database: Subtasks are added, ticked
/// through their Toggle pattern, moved and deleted; a Reminder is set, its error placed and its Snooze cleared;
/// the Repeat editor saves a Recurrence and deleting an Occurrence asks "this occurrence or stop repeating";
/// Tags are added with Enter and removed with their chip's own button.
/// </summary>
[TestClass]
public sealed class DetailPaneSectionsTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task Subtasks_are_added_with_Enter_ticked_through_their_Toggle_pattern_and_deleted()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Pack for the trip");
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var pane = await OpenPaneAsync(window, "Pack for the trip");

            var newBox = Named<TextBox>(pane, "New subtask title");
            newBox.Focus(FocusState.Programmatic);
            newBox.Text = "Passport";
            Assert.IsTrue(pane.TryShortcut(VirtualKey.Enter, VirtualKeyModifiers.None), "Enter did not add the Subtask.");
            await UiWait.UntilAsync(() => Shown<TextBox>(pane, "Subtask title").Any(b => b.Text == "Passport"),
                () => "The new Subtask has no row.", Ct);
            Assert.AreEqual("", newBox.Text);
            Assert.AreEqual("0 of 1", Named<TextBlock>(pane, "Subtask progress").Text);

            var done = Shown<CheckBox>(pane, "Subtask done").Single();
            var toggle = FrameworkElementAutomationPeer.CreatePeerForElement(done) as IToggleProvider;
            Assert.IsNotNull(toggle, "The Subtask checkbox has no Toggle pattern.");
            toggle.Toggle();
            await UiWait.UntilAsync(() => done.IsChecked == true, () => "Toggle did not tick the Subtask.", Ct);
            await viewModel.FlushPendingSaveAsync();
            Assert.IsTrue((await db.QueryAsync(c => c.Subtasks)).Single().IsCompleted);
            await UiWait.UntilAsync(() => Named<TextBlock>(pane, "Subtask progress").Text == "1 of 1",
                () => $"The progress reads '{Named<TextBlock>(pane, "Subtask progress").Text}'.", Ct);

            Invoke(Shown<Button>(pane, "Delete subtask").Single());
            await UiWait.UntilAsync(() => !Shown<TextBox>(pane, "Subtask title").Any(),
                () => "The deleted Subtask still has a row.", Ct);
            Assert.IsEmpty(await db.QueryAsync(c => c.Subtasks));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_Subtask_moves_up_and_down_and_its_title_is_edited_in_place()
    {
        using var db = await DatabaseAsync();
        var id = await AddAsync(db, "Pack for the trip");
        await db.Get<ITaskService>().AddSubtaskAsync(id, "Passport", Ct);
        await db.Get<ITaskService>().AddSubtaskAsync(id, "Tickets", Ct);
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var pane = await OpenPaneAsync(window, "Pack for the trip");
            await UiWait.UntilAsync(() => Titles(pane).Count == 2, () => "The Subtasks have no rows.", Ct);

            Invoke(Shown<Button>(pane, "Move subtask up")[1]);
            await UiWait.UntilAsync(() => Titles(pane).SequenceEqual(["Tickets", "Passport"]),
                () => $"The Subtasks read [{string.Join(", ", Titles(pane))}] after Move up.", Ct);

            Invoke(Shown<Button>(pane, "Move subtask down")[0]);
            await UiWait.UntilAsync(() => Titles(pane).SequenceEqual(["Passport", "Tickets"]),
                () => $"The Subtasks read [{string.Join(", ", Titles(pane))}] after Move down.", Ct);

            var title = Shown<TextBox>(pane, "Subtask title")[1];
            title.Focus(FocusState.Programmatic);
            title.Text = "Train tickets";
            Named<TextBox>(pane, "New subtask title").Focus(FocusState.Programmatic);
            await UiWait.UntilAsync(() => viewModel.SubtaskItems[1].Title == "Train tickets",
                () => "The Subtask title was not taken.", Ct);
            await viewModel.FlushPendingSaveAsync();
            CollectionAssert.AreEquivalent(new[] { "Passport", "Train tickets" },
                (await db.QueryAsync(c => c.Subtasks.Select(s => s.Title))).ToList());
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_Reminder_set_in_the_pane_is_saved_and_clearing_its_date_clears_it()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Call the bank");
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var pane = await OpenPaneAsync(window, "Call the bank");
            var time = VisualTree.Find<TimePicker>(pane, "ReminderTimePicker");
            Assert.AreEqual("Reminder time", AutomationProperties.GetName(time));
            Assert.IsNull(time.SelectedTime);

            var picker = Named<CalendarDatePicker>(pane, "Reminder date");
            picker.Date = new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Local));
            await UiWait.UntilAsync(() => time.IsEnabled, () => "Picking a Reminder date did not enable the time.", Ct);
            time.SelectedTime = new TimeSpan(14, 30, 0);
            await UiWait.UntilAsync(() => viewModel.DetailReminderTime == new TimeSpan(14, 30, 0),
                () => "The time was not taken.", Ct);
            await viewModel.FlushPendingSaveAsync();

            var saved = (await db.QueryAsync(c => c.Tasks)).Single();
            Assert.AreEqual(new DateTime(2026, 10, 20, 14, 30, 0, DateTimeKind.Utc), saved.ReminderUtc);

            picker.Date = null;
            await viewModel.FlushPendingSaveAsync();
            Assert.IsNull((await db.QueryAsync(c => c.Tasks)).Single().ReminderUtc, "Clearing the date kept the Reminder.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_Reminder_time_without_a_date_error_shows_right_under_the_Reminder_field_above_the_Priority_label()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Call the bank");
        var (window, viewModel) = await OpenAsync(db, height: 1000);
        try
        {
            var pane = await OpenPaneAsync(window, "Call the bank");

            viewModel.DetailReminderTime = new TimeSpan(14, 30, 0);
            pane.UpdateLayout();

            var error = ShownText(pane, "A Reminder time needs a Reminder date.");
            var picker = VisualTree.Find<TimePicker>(pane, "ReminderTimePicker");
            Assert.IsTrue(Top(error, pane) >= Bottom(picker, pane) - 0.5, "The error is above the Reminder field.");
            Assert.IsTrue(Bottom(error, pane) <= Top(Label(pane, "Priority"), pane) + 0.5,
                "The error is below the Priority label.");
            Assert.IsTrue(Top(Label(pane, "Reminder"), pane) >= Bottom(Named<CalendarDatePicker>(pane, "Due date"), pane),
                "Reminder is not under Due.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_snoozed_Reminder_shows_its_Snooze_and_Clear_Snooze_clears_it()
    {
        using var db = await DatabaseAsync();
        var id = await AddAsync(db, "Call the bank");
        var reminders = db.Get<IReminderService>();
        await reminders.SetReminderAsync(id, new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc), Ct);
        await reminders.SnoozeAsync(id, TimeSpan.FromMinutes(10), Ct);
        var (window, _) = await OpenAsync(db);
        try
        {
            var pane = await OpenPaneAsync(window, "Call the bank");
            ShownText(pane, "Snoozed until 09:40");

            Invoke(Shown<Button>(pane, "Clear Snooze").Single());

            var row = VisualTree.Find<Grid>(pane, "SnoozeRow");
            await UiWait.UntilAsync(() => row.Visibility == Visibility.Collapsed,
                () => "The Snooze stayed shown.", Ct);
            Assert.IsNull((await db.QueryAsync(c => c.Tasks)).Single().SnoozedUntilUtc);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Repeat_shows_only_with_a_Due_date_and_its_editor_saves_the_Recurrence()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Water plants");
        var (window, viewModel) = await OpenAsync(db, height: 1000);
        try
        {
            var pane = await OpenPaneAsync(window, "Water plants");
            var section = VisualTree.Find<StackPanel>(pane, "RepeatSection");
            Assert.AreEqual(Visibility.Collapsed, section.Visibility, "Repeat shows without a Due date.");

            Named<CalendarDatePicker>(pane, "Due date").Date =
                new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Local)); // a Monday
            await viewModel.FlushPendingSaveAsync(); // the Repeat defaults follow the saved Due date
            await UiWait.UntilAsync(() => section.Visibility == Visibility.Visible && section.ActualHeight > 0,
                () => "Repeat did not show once the Task had a Due date.", Ct);
            var repeat = Named<ComboBox>(pane, "Repeat");
            Assert.AreEqual("None", (string)repeat.SelectedItem);

            repeat.SelectedIndex = (int)RepeatPreset.Weekly;
            await UiWait.UntilAsync(() => Shown<CheckBox>(pane, "Monday").Count == 1,
                () => "Weekly shows no weekday boxes.", Ct);
            Assert.IsTrue(Shown<CheckBox>(pane, "Monday").Single().IsChecked, "The Due date's weekday is not ticked.");
            Shown<CheckBox>(pane, "Thursday").Single().IsChecked = true;
            var every = Shown<TextBox>(pane, "Repeat every").Single();
            every.Focus(FocusState.Programmatic);
            every.Text = "2";
            repeat.Focus(FocusState.Programmatic);
            await UiWait.UntilAsync(() => viewModel.Repeat.Interval == 2, () => "The interval was not taken.", Ct);
            Shown<CheckBox>(pane, "Repeat from the completion date").Single().IsChecked = true;
            Named<CalendarDatePicker>(pane, "Repeat end date").Date =
                new DateTimeOffset(new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Local));
            await viewModel.FlushPendingSaveAsync();

            var saved = (await db.QueryAsync(c => c.Tasks)).Single().Recurrence;
            Assert.IsNotNull(saved);
            Assert.AreEqual(RecurrenceFrequency.Weekly, saved.Frequency);
            Assert.AreEqual(2, saved.Interval);
            Assert.AreEqual(RecurrenceDays.Monday | RecurrenceDays.Thursday, saved.DaysOfWeek);
            Assert.AreEqual(RecurrenceAnchor.CompletionDate, saved.Anchor);
            Assert.AreEqual(new DateOnly(2026, 12, 31), saved.EndDate);

            repeat.SelectedIndex = (int)RepeatPreset.Monthly;
            await UiWait.UntilAsync(() => Shown<TextBox>(pane, "Day of month").Count == 1,
                () => "Monthly shows no day of month box.", Ct);
            var day = Shown<TextBox>(pane, "Day of month").Single();
            Assert.AreEqual("19", day.Text);
            day.Focus(FocusState.Programmatic);
            day.Text = "5";
            repeat.Focus(FocusState.Programmatic);
            await UiWait.UntilAsync(() => viewModel.Repeat.DayOfMonth == 5, () => "The day of month was not taken.", Ct);
            await viewModel.FlushPendingSaveAsync();
            var monthly = (await db.QueryAsync(c => c.Tasks)).Single().Recurrence;
            Assert.AreEqual(RecurrenceFrequency.Monthly, monthly?.Frequency);
            Assert.AreEqual(5, monthly?.DayOfMonth);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_Repeat_error_shows_under_the_Repeat_section_above_the_Priority_label()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Water plants", due: new DateOnly(2026, 10, 19));
        var (window, viewModel) = await OpenAsync(db, height: 1000);
        try
        {
            var pane = await OpenPaneAsync(window, "Water plants");

            viewModel.Repeat.Preset = RepeatPreset.Daily;
            viewModel.Repeat.Interval = 0;
            pane.UpdateLayout();

            var error = VisualTree.Find<TextBlock>(pane, "RepeatErrorText");
            await UiWait.UntilAsync(() => error.Text.Length > 0 && error.ActualHeight > 0,
                () => "No Repeat error shows.", Ct);
            Assert.IsTrue(Top(error, pane) >= Bottom(Named<ComboBox>(pane, "Repeat"), pane) - 0.5,
                "The error is above the Repeat field.");
            Assert.IsTrue(Bottom(error, pane) <= Top(Label(pane, "Priority"), pane) + 0.5,
                "The error is below the Priority label.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Deleting_a_repeating_Task_from_the_pane_asks_this_occurrence_or_stop_repeating()
    {
        using var db = await DatabaseAsync();
        var id = await AddAsync(db, "Water plants", due: new DateOnly(2026, 10, 19));
        await db.Get<IRecurrenceService>().SetRecurrenceAsync(id, Recurrence.Weekly(RecurrenceDays.Monday, 1), Ct);
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var pane = await OpenPaneAsync(window, "Water plants");
            var delete = Shown<Button>(pane, "Delete Task").Single();

            Invoke(delete);
            var dialog = await DialogHost.OpenDialogAsync(window.Content.XamlRoot, Ct);
            CollectionAssert.AreEqual(new[] { "Delete this occurrence", "Stop repeating", "Cancel" },
                DialogHost.Buttons(dialog).Select(b => b.Content).ToList());
            DialogHost.Press(dialog, "CloseButton");
            await UiWait.UntilAsync(() => DialogHost.OpenDialog(window.Content.XamlRoot) is null,
                () => "Cancel left the dialog open.", Ct);
            Assert.HasCount(1, await db.QueryAsync(c => c.Tasks), "Cancel deleted the Task.");
            await UiWait.UntilAsync(() => delete.IsEnabled, () => "Delete Task stayed disabled after Cancel.", Ct);

            Invoke(delete);
            dialog = await DialogHost.OpenDialogAsync(window.Content.XamlRoot, Ct);
            DialogHost.Press(dialog, "PrimaryButton");
            await UiWait.UntilAsync(() => viewModel.Tasks.Any(t => t.Id != id),
                () => "Skipping the Occurrence produced no next one.", Ct);
            var left = await db.QueryAsync(c => c.Tasks);
            Assert.HasCount(1, left);
            Assert.AreEqual(new DateOnly(2026, 10, 26), left[0].DueDate);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_Tag_is_added_with_Enter_in_the_new_Tag_box_and_removed_only_by_its_chip_button()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var pane = await OpenPaneAsync(window, "Book dentist");
            var box = VisualTree.Find<TextBox>(pane, "NewTagBox");
            Assert.AreEqual("NewTagBox", AutomationProperties.GetAutomationId(box));
            Assert.AreEqual("New tag name", AutomationProperties.GetName(box));

            box.Focus(FocusState.Programmatic);
            box.Text = "health";
            Assert.IsTrue(pane.TryShortcut(VirtualKey.Enter, VirtualKeyModifiers.None), "Enter did not add the Tag.");
            var chips = Named<ItemsControl>(pane, "Task tags");
            await UiWait.UntilAsync(() => Shown<Button>(chips, "Remove #health").Count == 1,
                () => "The new Tag has no chip.", Ct);
            Assert.AreEqual("", box.Text);
            Assert.HasCount(1, await db.QueryAsync(c => c.TaskTags));
            var sidebarTags = VisualTree.Find<ListView>((FrameworkElement)window.Content, "TagList");
            await UiWait.UntilAsync(() => sidebarTags.Items.OfType<Tag>().Any(t => t.Name == "health"),
                () => "The sidebar's Tags do not list the new Tag.", Ct);

            var remove = Shown<Button>(chips, "Remove #health").Single();
            var chipTexts = VisualTree.Descendants(chips).OfType<TextBlock>().Select(t => t.Text)
                .Except(VisualTree.Descendants(remove).OfType<TextBlock>().Select(t => t.Text)).ToList();
            CollectionAssert.AreEqual(new[] { "#health" }, chipTexts);
            Assert.AreEqual("Remove tag", ToolTipService.GetToolTip(remove));
            Assert.AreEqual(1, VisualTree.Descendants(chips).OfType<ButtonBase>().Count(), "The chip has more than one button.");

            Invoke(remove);
            await UiWait.UntilAsync(() => !Shown<Button>(chips, "Remove #health").Any(),
                () => "The removed Tag still has a chip.", Ct);
            Assert.IsEmpty(await db.QueryAsync(c => c.TaskTags));
            Assert.AreEqual("Book dentist", viewModel.DetailTask?.Title);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Tag_chips_wrap_onto_the_next_line_when_the_pane_is_narrow()
    {
        var panel = new WrapPanel
        {
            HorizontalSpacing = 4, VerticalSpacing = 4, Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        for (var i = 0; i < 3; i++)
        {
            panel.Children.Add(new Border { Width = 50, Height = 20 });
        }

        using var hosted = await Hosted.ShowAsync(panel, Ct);

        Assert.AreEqual(44, panel.ActualHeight, "Three 50 px chips in 120 px do not take two lines.");
        Assert.AreEqual(new Point(54, 0), Offset(panel.Children[1], panel));
        Assert.AreEqual(new Point(0, 24), Offset(panel.Children[2], panel));
    }

    private static Point Offset(UIElement element, UIElement root) =>
        element.TransformToVisual(root).TransformPoint(new Point(0, 0));

    private static void Invoke(Button button) => new ButtonAutomationPeer(button).Invoke();

    private static T Named<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        VisualTree.Descendants(root).OfType<T>().Single(e => AutomationProperties.GetName(e) == name);

    // The laid-out elements with an accessible name, in tree order; a collapsed one has no size.
    private static List<T> Shown<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        VisualTree.Descendants(root).OfType<T>()
            .Where(e => AutomationProperties.GetName(e) == name && e.ActualHeight > 0).ToList();

    private static List<string> Titles(FrameworkElement pane) =>
        Shown<TextBox>(pane, "Subtask title").Select(b => b.Text).ToList();

    private static TextBlock Label(FrameworkElement root, string text) =>
        VisualTree.Descendants(root).OfType<TextBlock>().Single(t => t.Text == text);

    private static TextBlock ShownText(FrameworkElement root, string text)
    {
        var shown = VisualTree.Descendants(root).OfType<TextBlock>()
            .Where(t => t.Text == text && t.Visibility == Visibility.Visible && t.ActualHeight > 0).ToList();
        Assert.HasCount(1, shown, $"'{text}' is not shown exactly once.");
        return shown[0];
    }

    private static double Top(FrameworkElement element, UIElement root) =>
        element.TransformToVisual(root).TransformPoint(new Point(0, 0)).Y;

    private static double Bottom(FrameworkElement element, UIElement root) => Top(element, root) + element.ActualHeight;

    // Selects the Task's row as a click would and waits for the docked pane to lay out.
    private async Task<DetailPane> OpenPaneAsync(MainWindow window, string title)
    {
        var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
        await UiWait.UntilAsync(
            () => VisualTree.Descendants(list).OfType<ListViewItem>().Any(i => AutomationProperties.GetName(i) == title),
            () => $"No row '{title}'.", Ct);
        list.SelectedItem = window.ViewModel.Tasks.Single(t => t.Title == title);
        var pane = window.Details.Pane;
        await UiWait.UntilAsync(() => pane.ActualWidth > 0, () => "The pane did not show.", Ct);
        pane.UpdateLayout();
        return pane;
    }

    private async Task<(MainWindow Window, MainViewModel ViewModel)> OpenAsync(TempDatabase db, double height = 900)
    {
        MainWindow? window = null;
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock,
            dueService: db.Get<IDueService>(), searchService: db.Get<ITaskSearchService>(),
            recurrenceService: db.Get<IRecurrenceService>(),
            occurrenceDialogs: new WinUiDialogs(new DialogService(() => window)),
            reminderService: db.Get<IReminderService>(),
            listService: db.Get<IListService>());
        window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await UiWait.UntilAsync(() => window.Content is FrameworkElement { IsLoaded: true },
                () => "The window did not load.", Ct);
            var content = (FrameworkElement)window.Content;
            var scale = content.XamlRoot.RasterizationScale;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(
                (int)Math.Round(1200 * scale), (int)Math.Round(height * scale)));
            await UiWait.UntilAsync(() => Math.Abs(content.ActualWidth - 1200) < 1,
                () => $"The content is {content.ActualWidth} px wide, not 1200.", Ct);
            await viewModel.LoadAsync(Ct);
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
        db.Clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return db;
    }

    private static async Task<Guid> AddAsync(TempDatabase db, string title, DateOnly? due = null)
    {
        var listId = (await db.QueryAsync(c => c.Lists.Where(l => l.IsDefault))).Single().Id;
        var order = (await db.QueryAsync(c => c.Tasks)).Count;
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
            SortOrder = order,
            CreatedUtc = now,
            UpdatedUtc = now,
        });
        await context.SaveChangesAsync();
        return id;
    }
}
