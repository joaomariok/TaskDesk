using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;
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
/// The detail pane's Subtasks, Reminders, Repeat and Tags over a real database: Subtasks are added, ticked
/// through their Toggle pattern, moved and deleted; Reminders are added and edited in their flyout, listed as
/// rows and removed, their errors placed and their Snooze cleared;
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
    public async Task A_Reminder_added_in_the_flyout_is_saved_listed_as_a_row_and_removed_with_its_x()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Call the bank");
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var pane = await OpenPaneAsync(window, "Call the bank");
            Assert.IsEmpty(ReminderRows(pane));

            var (flyout, editor) = await OpenReminderFlyoutAsync(pane, Named<Button>(pane, "Add reminder"));
            var time = VisualTree.Find<TimePicker>(editor, "ReminderTimePicker");
            Assert.AreEqual("Reminder time", AutomationProperties.GetName(time));
            Assert.IsNull(time.SelectedTime);
            VisualTree.Find<CalendarDatePicker>(editor, "ReminderDatePicker").Date =
                new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Local));
            time.SelectedTime = new TimeSpan(14, 30, 0);
            Invoke(VisualTree.Find<Button>(editor, "SaveReminderButton"));

            await UiWait.UntilAsync(() => !flyout.IsOpen, () => "Save did not close the flyout.", Ct);
            await UiWait.UntilAsync(() => ReminderRows(pane).Count == 1, () => "The new Reminder has no row.", Ct);
            await viewModel.FlushPendingSaveAsync();
            Assert.AreEqual(
                new DateTime(2026, 10, 20, 14, 30, 0, DateTimeKind.Utc),
                (await db.QueryAsync(c => c.Reminders)).Single().AtUtc);
            var row = viewModel.DetailReminders.Single();
            Assert.AreEqual(row.AccessibleName, AutomationProperties.GetName(ReminderRows(pane)[0]));

            Invoke(Named<Button>(pane, row.RemoveName));
            await UiWait.UntilAsync(() => ReminderRows(pane).Count == 0, () => "The removed Reminder kept its row.", Ct);
            await viewModel.FlushPendingSaveAsync();
            Assert.IsEmpty(await db.QueryAsync(c => c.Reminders), "The x kept the Reminder.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Closing_the_Reminder_flyout_without_Save_saves_nothing_and_returns_focus_to_its_opener()
    {
        using var db = await DatabaseAsync();
        var id = await AddAsync(db, "Call the bank");
        await db.Get<IReminderService>().AddReminderAsync(
            id, ReminderValue.Fixed(new DateTime(2026, 10, 20, 14, 30, 0, DateTimeKind.Utc)), Ct);
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var pane = await OpenPaneAsync(window, "Call the bank");
            await UiWait.UntilAsync(() => ReminderRows(pane).Count == 1, () => "The Reminder has no row.", Ct);

            // A click on the row opens the flyout on that Reminder; closing it (Esc) leaves it as it was.
            var rowButton = ReminderRows(pane)[0];
            var (flyout, editor) = await OpenReminderFlyoutAsync(pane, rowButton);
            var time = VisualTree.Find<TimePicker>(editor, "ReminderTimePicker");
            Assert.AreEqual(new TimeSpan(14, 30, 0), time.SelectedTime);
            Assert.IsTrue(viewModel.ReminderEditor.IsEditing);
            time.SelectedTime = new TimeSpan(8, 0, 0);
            flyout.Hide();

            await UiWait.UntilAsync(() => !viewModel.ReminderEditor.IsOpen, () => "Closing kept the editor open.", Ct);
            await UiWait.UntilAsync(
                () => ReferenceEquals(FocusManager.GetFocusedElement(pane.XamlRoot), rowButton),
                () => $"The focus went to {FocusManager.GetFocusedElement(pane.XamlRoot)}, not the row.", Ct);
            await viewModel.FlushPendingSaveAsync();
            Assert.AreEqual(
                new DateTime(2026, 10, 20, 14, 30, 0, DateTimeKind.Utc),
                (await db.QueryAsync(c => c.Reminders)).Single().AtUtc);

            // "Add reminder" gets the focus back the same way.
            var add = Named<Button>(pane, "Add reminder");
            (flyout, _) = await OpenReminderFlyoutAsync(pane, add);
            flyout.Hide();
            await UiWait.UntilAsync(() => !viewModel.ReminderEditor.IsOpen, () => "Closing kept the editor open.", Ct);
            await UiWait.UntilAsync(
                () => ReferenceEquals(FocusManager.GetFocusedElement(pane.XamlRoot), add),
                () => $"The focus went to {FocusManager.GetFocusedElement(pane.XamlRoot)}, not Add reminder.", Ct);
            Assert.HasCount(1, await db.QueryAsync(c => c.Reminders));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_past_Reminder_row_is_dimmed_struck_through_announced_as_past_and_each_Task_shows_its_own_rows()
    {
        using var db = await DatabaseAsync();
        var id = await AddAsync(db, "Call the bank");
        await AddAsync(db, "Plain");
        var reminders = db.Get<IReminderService>();
        var now = db.Clock.GetUtcNow().UtcDateTime;
        await reminders.AddReminderAsync(id, ReminderValue.Fixed(now.AddHours(1)), Ct);
        await reminders.AddReminderAsync(id, ReminderValue.Fixed(now.AddHours(2)), Ct);
        db.Clock.Advance(TimeSpan.FromMinutes(90));
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var pane = await OpenPaneAsync(window, "Call the bank");
            await UiWait.UntilAsync(() => ReminderRows(pane).Count == 2, () => "The Reminders have no rows.", Ct);
            var names = ReminderRows(pane).Select(AutomationProperties.GetName).ToList();
            StringAssert.EndsWith(names[0], ", past");
            Assert.IsFalse(names[1].Contains("past", StringComparison.Ordinal), names[1]);

            // The past row looks like a completed Task: dimmed and struck through, with no "past" word.
            var past = RowText(ReminderRows(pane)[0]);
            var ahead = RowText(ReminderRows(pane)[1]);
            Assert.AreEqual(Windows.UI.Text.TextDecorations.Strikethrough, past.TextDecorations);
            Assert.IsLessThan(1.0, past.Opacity, "The past row is not dimmed.");
            Assert.AreEqual(Windows.UI.Text.TextDecorations.None, ahead.TextDecorations);
            Assert.AreEqual(1.0, ahead.Opacity);
            foreach (var row in ReminderRows(pane))
            {
                Assert.IsFalse(
                    VisualTree.Descendants(row).OfType<TextBlock>().Any(t => t.Text.Contains("past", StringComparison.Ordinal)),
                    "A row shows the word past.");
            }

            // A past row still opens the flyout on its Reminder.
            var (flyout, _) = await OpenReminderFlyoutAsync(pane, ReminderRows(pane)[0]);
            Assert.IsTrue(viewModel.ReminderEditor.IsEditing, "The past row did not open its Reminder.");
            flyout.Hide();
            await UiWait.UntilAsync(() => !viewModel.ReminderEditor.IsOpen, () => "Closing kept the editor open.", Ct);

            await OpenPaneAsync(window, "Plain");
            await UiWait.UntilAsync(() => ReminderRows(pane).Count == 0, () => "Another Task kept the rows.", Ct);
            await OpenPaneAsync(window, "Call the bank");
            await UiWait.UntilAsync(() => ReminderRows(pane).Count == 2, () => "The rows did not come back.", Ct);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_Reminders_box_shows_only_with_Reminders_holds_its_rows_and_Add_reminder_sits_below_it()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Call the bank");
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var pane = await OpenPaneAsync(window, "Call the bank");
            var box = VisualTree.Find<Border>(pane, "ReminderBox");
            var add = Named<Button>(pane, "Add reminder");
            Assert.AreEqual(Visibility.Collapsed, box.Visibility, "The box shows without a Reminder.");
            var addLeft = Offset(add, pane).X;

            var (flyout, editor) = await OpenReminderFlyoutAsync(pane, add);
            viewModel.ReminderEditor.Date = new DateTime(2026, 10, 20);
            viewModel.ReminderEditor.Time = new TimeSpan(14, 30, 0);
            Invoke(VisualTree.Find<Button>(editor, "SaveReminderButton"));
            await UiWait.UntilAsync(() => !flyout.IsOpen, () => "Save did not close the flyout.", Ct);
            await UiWait.UntilAsync(() => box.Visibility == Visibility.Visible && ReminderRows(pane).Count == 1,
                () => "The box did not show with the new Reminder.", Ct);
            pane.UpdateLayout();

            Assert.AreEqual(new Thickness(1), box.BorderThickness);
            Assert.AreEqual(new CornerRadius(4), box.CornerRadius);
            var row = (FrameworkElement)VisualTree.Descendants(box).OfType<Grid>()
                .First(g => g.DataContext is ReminderRowViewModel);
            var inside = Bounds(row, box);
            Assert.IsTrue(
                inside.Left > 1 && inside.Top > 1 && inside.Right < box.ActualWidth - 1
                    && inside.Bottom < box.ActualHeight - 1,
                $"The row at {inside} touches the box outline ({box.ActualWidth} x {box.ActualHeight}).");
            Assert.IsGreaterThanOrEqualTo(Bottom(box, pane), Top(add, pane), "Add reminder is not below the box.");
            Assert.AreEqual(addLeft, Offset(add, pane).X, 0.5, "Add reminder moved sideways with the box.");
            Assert.AreEqual(Offset(box, pane).X, Offset(add, pane).X, 0.5, "Add reminder is not aligned with the box.");
            Assert.AreEqual(viewModel.DetailReminders[0].Description,
                ToolTipService.GetToolTip(ReminderRows(pane)[0]) as string, "The row's tooltip is not its full wording.");

            Invoke(Named<Button>(pane, viewModel.DetailReminders[0].RemoveName));
            await UiWait.UntilAsync(() => box.Visibility == Visibility.Collapsed,
                () => "The box stayed with no Reminder left.", Ct);
            Assert.AreEqual(addLeft, Offset(add, pane).X, 0.5, "Add reminder moved sideways when the box hid.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Each_Reminder_row_fits_one_line_in_the_320_px_pane_and_a_long_one_ends_in_an_ellipsis()
    {
        using var db = await DatabaseAsync();
        var rent = await AddAsync(db, "Pay rent", new DateOnly(2026, 10, 26));
        var passport = await AddAsync(db, "Renew passport", new DateOnly(2027, 12, 29));
        var reminders = db.Get<IReminderService>();
        await reminders.AddReminderAsync(rent, ReminderValue.BeforeDue(1, ReminderUnit.Days), Ct);
        await reminders.AddReminderAsync(passport, ReminderValue.BeforeDue(999, ReminderUnit.Weeks), Ct);
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var pane = await OpenPaneAsync(window, "Pay rent");
            Assert.AreEqual(PaneLayout.DetailLimits.Default, pane.ActualWidth, 1, "The pane is not at its 320 px default.");
            await UiWait.UntilAsync(() => ReminderRows(pane).Count == 1, () => "The Reminder has no row.", Ct);
            pane.UpdateLayout();
            StringAssert.StartsWith(viewModel.DetailReminders[0].Text, "1 day before · ");
            var (text, width, needed, trimmed) = CheckRowInsideBox(pane);
            Assert.IsFalse(trimmed, $"'{text}' is trimmed at the 320 px pane.");
            Assert.IsLessThanOrEqualTo(width + 1, needed, $"'{text}' needs {needed} px but has {width}.");

            await OpenPaneAsync(window, "Renew passport");
            await UiWait.UntilAsync(
                () => ReminderRows(pane).Count == 1 && viewModel.DetailReminders[0].Text.StartsWith("999 weeks", StringComparison.Ordinal),
                () => "The long Reminder has no row.", Ct);
            pane.UpdateLayout();
            (text, width, needed, trimmed) = CheckRowInsideBox(pane);
            Assert.IsGreaterThan(width + 1, needed, $"'{text}' ({needed} px) is not long enough to need trimming.");
            Assert.IsTrue(trimmed, $"'{text}' does not end in an ellipsis.");
        }
        finally
        {
            window.Close();
        }
    }

    // Checks the single row's text, its x and the box stay inside one another and the pane, all on one line, and
    // returns the text, the width it has, the width it needs unbounded and whether it is trimmed.
    private static (string Text, double Width, double Needed, bool Trimmed) CheckRowInsideBox(DetailPane pane)
    {
        var box = VisualTree.Find<Border>(pane, "ReminderBox");
        var rowButton = ReminderRows(pane).Single();
        var text = RowText(rowButton);
        var remove = Named<Button>(pane, ((ReminderRowViewModel)rowButton.DataContext).RemoveName);
        var textBounds = Bounds(text, pane);
        var removeBounds = Bounds(remove, pane);
        var boxBounds = Bounds(box, pane);
        Assert.IsLessThanOrEqualTo(removeBounds.Left + 0.5, textBounds.Right, $"'{text.Text}' runs under its x.");
        Assert.IsLessThanOrEqualTo(boxBounds.Right - 1, removeBounds.Right, "The x runs past the box.");
        Assert.IsLessThanOrEqualTo(pane.ActualWidth + 0.5, boxBounds.Right, "The box runs past the pane.");
        Assert.IsGreaterThanOrEqualTo(boxBounds.Left + 1, textBounds.Left, "The text starts outside the box.");
        Assert.AreEqual(TextWrapping.NoWrap, text.TextWrapping);
        var isTrimmed = text.IsTextTrimmed;
        var width = text.ActualWidth;

        // Measured without a limit, the text's own width; the measure rounds up to whole pixels.
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return (text.Text, width, text.DesiredSize.Width, isTrimmed);
    }

    private static Rect Bounds(FrameworkElement element, UIElement root) =>
        element.TransformToVisual(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    [UITestMethod]
    public async Task Every_Reminder_control_is_reachable_with_Tab_and_has_a_name()
    {
        using var db = await DatabaseAsync();
        var id = await AddAsync(db, "Call the bank");
        await db.Get<IReminderService>().AddReminderAsync(
            id, ReminderValue.Fixed(new DateTime(2026, 10, 20, 14, 30, 0, DateTimeKind.Utc)), Ct);
        var (window, _) = await OpenAsync(db);
        try
        {
            var pane = await OpenPaneAsync(window, "Call the bank");
            await UiWait.UntilAsync(() => ReminderRows(pane).Count == 1, () => "The Reminder has no row.", Ct);

            var row = ReminderRows(pane)[0];
            var remove = Named<Button>(pane, "Remove reminder " + viewModelText(row));
            foreach (var control in new Control[] { row, remove, Named<Button>(pane, "Add reminder") })
            {
                Assert.IsTrue(control.IsTabStop && control.Focus(FocusState.Keyboard),
                    $"{AutomationProperties.GetName(control)} takes no keyboard focus.");
            }

            var (flyout, editor) = await OpenReminderFlyoutAsync(pane, Named<Button>(pane, "Add reminder"));
            window.ViewModel.ReminderEditor.Date = new DateTime(2026, 10, 21); // the time needs a date
            editor.UpdateLayout();
            foreach (var name in new[]
            {
                "ReminderDatePicker", "ReminderTimePicker", "ClearReminderTimeButton", "CancelReminderButton",
                "SaveReminderButton",
            })
            {
                var control = VisualTree.Find<Control>(editor, name);
                Assert.IsFalse(string.IsNullOrEmpty(AutomationProperties.GetName(control)), $"{name} has no name.");
                if (name != "ClearReminderTimeButton") // enabled only once there is a time to clear
                {
                    Assert.IsTrue(control.Focus(FocusState.Keyboard), $"{name} takes no keyboard focus.");
                }
            }

            Assert.IsTrue(VisualTree.Descendants(editor).OfType<TextBlock>().Any(t => t.Text == "Date"), "No Date label.");
            Assert.IsTrue(
                VisualTree.Descendants(editor).OfType<TextBlock>().Any(t => t.Text == "Time"), "No Time label.");
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }

        static string viewModelText(Button row) => ((ReminderRowViewModel)row.DataContext).Text;
    }

    [UITestMethod]
    public async Task A_Reminder_time_without_a_date_error_shows_right_under_the_Reminders_above_the_Priority_label()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Call the bank");
        var (window, viewModel) = await OpenAsync(db, height: 1000);
        try
        {
            var pane = await OpenPaneAsync(window, "Call the bank");

            viewModel.AddReminderCommand.Execute(null);
            viewModel.ReminderEditor.Time = new TimeSpan(14, 30, 0);
            viewModel.ReminderEditor.SaveCommand.Execute(null);
            pane.UpdateLayout();

            var error = ShownText(pane, "A Reminder time needs a Reminder date.");
            var add = Named<Button>(pane, "Add reminder");
            Assert.IsTrue(Top(error, pane) >= Bottom(add, pane) - 0.5, "The error is above the Reminders.");
            Assert.IsTrue(Bottom(error, pane) <= Top(Label(pane, "Priority"), pane) + 0.5,
                "The error is below the Priority label.");
            Assert.IsTrue(Top(Label(pane, "Reminders"), pane) >= Bottom(Named<CalendarDatePicker>(pane, "Due date"), pane),
                "Reminders are not under Due.");
            viewModel.ReminderEditor.CancelCommand.Execute(null);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_duplicate_Reminder_error_shows_under_the_Reminders()
    {
        using var db = await DatabaseAsync();
        var id = await AddAsync(db, "Call the bank");
        await db.Get<IReminderService>().AddReminderAsync(
            id, ReminderValue.Fixed(new DateTime(2026, 10, 20, 14, 30, 0, DateTimeKind.Utc)), Ct);
        var (window, viewModel) = await OpenAsync(db, height: 1000);
        try
        {
            var pane = await OpenPaneAsync(window, "Call the bank");
            await UiWait.UntilAsync(() => ReminderRows(pane).Count == 1, () => "The Reminder has no row.", Ct);

            viewModel.AddReminderCommand.Execute(null);
            viewModel.ReminderEditor.Date = new DateTime(2026, 10, 20);
            viewModel.ReminderEditor.Time = new TimeSpan(14, 30, 0);
            viewModel.ReminderEditor.SaveCommand.Execute(null);
            await viewModel.FlushPendingSaveAsync();
            pane.UpdateLayout();

            var error = ShownText(pane, "The Task already has this Reminder.");
            Assert.IsTrue(Top(error, pane) >= Bottom(Named<Button>(pane, "Add reminder"), pane) - 0.5,
                "The error is above the Reminders.");
            Assert.HasCount(1, ReminderRows(pane));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Before_Due_is_off_without_a_Due_and_its_presets_and_Custom_show_in_the_flyout()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "No due");
        await AddAsync(db, "Pay rent", new DateOnly(2026, 10, 20));
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            // Without a Due, the Before Due mode is unavailable and the date and time show.
            var pane = await OpenPaneAsync(window, "No due");
            var (flyout, editor) = await OpenReminderFlyoutAsync(pane, Named<Button>(pane, "Add reminder"));
            var beforeDue = VisualTree.Find<RadioButton>(editor, "BeforeDueModeButton");
            Assert.IsFalse(beforeDue.IsEnabled, "Before Due is available without a Due.");
            Assert.AreEqual(Visibility.Visible, VisualTree.Find<StackPanel>(editor, "DateAndTimePanel").Visibility);
            Assert.AreEqual(Visibility.Collapsed, VisualTree.Find<StackPanel>(editor, "BeforeDuePanel").Visibility);
            flyout.Hide();
            await UiWait.UntilAsync(() => !viewModel.ReminderEditor.IsOpen, () => "Closing kept the editor open.", Ct);

            // With a Due it is available; picking it swaps the date and time for the presets.
            pane = await OpenPaneAsync(window, "Pay rent");
            (flyout, editor) = await OpenReminderFlyoutAsync(pane, Named<Button>(pane, "Add reminder"));
            Assert.IsTrue(beforeDue.IsEnabled, "Before Due is unavailable with a Due.");
            var mode = VisualTree.Find<RadioButtons>(editor, "ReminderModeButtons");
            mode.SelectedIndex = ReminderEditorViewModel.BeforeDueMode;
            await UiWait.UntilAsync(
                () => VisualTree.Find<StackPanel>(editor, "BeforeDuePanel").ActualHeight > 0,
                () => "The Before Due choices did not show.", Ct);
            Assert.AreEqual(Visibility.Collapsed, VisualTree.Find<StackPanel>(editor, "DateAndTimePanel").Visibility);
            var offset = VisualTree.Find<ComboBox>(editor, "BeforeDueOffsetBox");
            Assert.AreEqual("Time before Due", AutomationProperties.GetName(offset));
            CollectionAssert.AreEqual(
                ReminderEditorViewModel.OffsetChoices.ToList(), offset.Items.Cast<string>().ToList());
            Assert.AreEqual(Visibility.Collapsed, VisualTree.Find<Grid>(editor, "CustomOffsetRow").Visibility);

            // "Custom…" shows the number and the unit; a preset hides them again.
            offset.SelectedIndex = ReminderEditorViewModel.CustomIndex;
            await UiWait.UntilAsync(
                () => VisualTree.Find<Grid>(editor, "CustomOffsetRow").ActualHeight > 0,
                () => "Custom did not show its number and unit.", Ct);
            var amount = VisualTree.Find<NumberBox>(editor, "CustomAmountBox");
            var unit = VisualTree.Find<ComboBox>(editor, "CustomUnitBox");
            Assert.AreEqual("Number before Due", AutomationProperties.GetName(amount));
            Assert.AreEqual("Unit before Due", AutomationProperties.GetName(unit));
            Assert.IsTrue(amount.Focus(FocusState.Keyboard) && unit.Focus(FocusState.Keyboard), "Custom takes no focus.");
            offset.SelectedIndex = 5; // 1 day
            await UiWait.UntilAsync(
                () => VisualTree.Find<Grid>(editor, "CustomOffsetRow").Visibility == Visibility.Collapsed,
                () => "A preset kept the Custom number and unit.", Ct);

            Invoke(VisualTree.Find<Button>(editor, "SaveReminderButton"));
            await UiWait.UntilAsync(() => !flyout.IsOpen, () => "Save did not close the flyout.", Ct);
            await UiWait.UntilAsync(() => ReminderRows(pane).Count == 1, () => "The new Reminder has no row.", Ct);
            await viewModel.FlushPendingSaveAsync();
            Assert.AreEqual(
                ReminderValue.BeforeDue(1, ReminderUnit.Days), (await db.QueryAsync(c => c.Reminders)).Single().Value);
            StringAssert.StartsWith(viewModel.DetailReminders.Single().Text, "1 day before · ");

            // A click on the row opens the flyout in the Before Due mode on that preset.
            (flyout, editor) = await OpenReminderFlyoutAsync(pane, ReminderRows(pane)[0]);
            Assert.AreEqual(ReminderEditorViewModel.BeforeDueMode, mode.SelectedIndex);
            Assert.AreEqual(5, offset.SelectedIndex);
            flyout.Hide();
            await UiWait.UntilAsync(() => !viewModel.ReminderEditor.IsOpen, () => "Closing kept the editor open.", Ct);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Clearing_the_Due_removes_the_before_Due_rows_and_keeps_the_fixed_one()
    {
        using var db = await DatabaseAsync();
        var id = await AddAsync(db, "Pay rent", new DateOnly(2026, 10, 20));
        var reminders = db.Get<IReminderService>();
        await reminders.AddReminderAsync(id, ReminderValue.BeforeDue(2, ReminderUnit.Hours), Ct);
        await reminders.AddReminderAsync(id, ReminderValue.Fixed(new DateTime(2026, 10, 18, 8, 0, 0, DateTimeKind.Utc)), Ct);
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var pane = await OpenPaneAsync(window, "Pay rent");
            await UiWait.UntilAsync(() => ReminderRows(pane).Count == 2, () => "The Reminders have no rows.", Ct);

            // Moving the Due moves the before-Due row's moment.
            viewModel.DetailDueDate = new DateTime(2026, 10, 21);
            await UiWait.UntilAsync(
                () => viewModel.DetailReminders.Any(r => r.FiresAtUtc == new DateTime(2026, 10, 21, 7, 0, 0, DateTimeKind.Utc)),
                () => "The before-Due row did not move with the Due.", Ct);

            VisualTree.Find<CalendarDatePicker>(pane, "DetailDueDatePicker").Date = null;
            await UiWait.UntilAsync(() => ReminderRows(pane).Count == 1, () => "The before-Due row stayed.", Ct);
            await viewModel.FlushPendingSaveAsync();
            Assert.AreEqual(ReminderKind.Fixed, (await db.QueryAsync(c => c.Reminders)).Single().Kind);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Notify_at_Due_shows_only_with_a_Due_and_saves_at_once()
    {
        using var db = await DatabaseAsync();
        var id = await AddAsync(db, "Pay rent");
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var pane = await OpenPaneAsync(window, "Pay rent");
            var box = VisualTree.Find<CheckBox>(pane, "NotifyAtDueBox");
            Assert.AreEqual(Visibility.Collapsed, box.Visibility, "Notify at Due shows without a Due.");
            Assert.AreEqual("Notify at Due", box.Content as string, "No visible label.");
            Assert.AreEqual("Notify at Due", AutomationProperties.GetName(box));

            Named<CalendarDatePicker>(pane, "Due date").Date =
                new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Local));
            await UiWait.UntilAsync(() => box.Visibility == Visibility.Visible && box.ActualHeight > 0,
                () => "Notify at Due did not show once the Task had a Due.", Ct);
            Assert.IsTrue(box.IsChecked, "Notify at Due is off for a new Task.");

            var toggle = (IToggleProvider)FrameworkElementAutomationPeer.CreatePeerForElement(box)
                .GetPattern(PatternInterface.Toggle);
            toggle.Toggle();
            await UiWait.UntilAsync(() => !viewModel.DetailNotifyAtDue, () => "The switch did not turn off.", Ct);
            await viewModel.PendingSave; // saved at once: nothing debounced to flush
            Assert.IsFalse((await db.QueryAsync(c => c.Tasks.Where(t => t.Id == id))).Single().NotifyAtDue,
                "Switching Notify at Due off did not save.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Nag_shows_only_with_a_Due_saves_at_once_and_shows_Off_when_the_Due_is_cleared()
    {
        using var db = await DatabaseAsync();
        var id = await AddAsync(db, "Pay rent");
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var pane = await OpenPaneAsync(window, "Pay rent");
            var section = VisualTree.Find<StackPanel>(pane, "NagSection");
            var box = VisualTree.Find<ComboBox>(pane, "NagBox");
            Assert.AreEqual(Visibility.Collapsed, section.Visibility, "Nag shows without a Due.");
            Assert.AreEqual("Nag", AutomationProperties.GetName(box));
            Assert.IsTrue(
                VisualTree.Descendants(section).OfType<TextBlock>().Any(t => t.Text == "Nag"), "Nag has no visible label.");
            CollectionAssert.AreEqual(
                new[] { "Off", "Every 15 min", "Every 30 min", "Every hour", "Every 3 hours", "Every day" },
                box.Items.Cast<string>().ToArray());

            var dueDate = Named<CalendarDatePicker>(pane, "Due date");
            dueDate.Date = new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Local));
            await UiWait.UntilAsync(() => section.Visibility == Visibility.Visible && box.ActualHeight > 0,
                () => "Nag did not show once the Task had a Due.", Ct);
            Assert.AreEqual("Off", box.SelectedItem as string, "A new Task Nags.");

            box.SelectedIndex = 3; // Every hour
            await UiWait.UntilAsync(() => viewModel.DetailNagPeriod == NagPeriod.OneHour,
                () => "Picking Every hour did not reach the view model.", Ct);
            await viewModel.PendingSave; // saved at once: nothing debounced to flush
            Assert.AreEqual(NagPeriod.OneHour, (await db.QueryAsync(c => c.Tasks.Where(t => t.Id == id))).Single().NagPeriod,
                "Picking a Nag did not save.");

            dueDate.Date = null;
            await UiWait.UntilAsync(() => viewModel.DetailNagPeriod is null && section.Visibility == Visibility.Collapsed,
                () => "Clearing the Due did not turn the Nag off.", Ct);
            await viewModel.PendingSave;
            Assert.AreEqual("Off", box.SelectedItem as string, "The pane does not show Off.");
            Assert.IsNull((await db.QueryAsync(c => c.Tasks.Where(t => t.Id == id))).Single().NagPeriod);
        }
        finally
        {
            window.Close();
        }
    }

    // The realized row buttons of the Reminders list, which open the flyout on their Reminder.
    private static List<Button> ReminderRows(DetailPane pane)
    {
        var list = VisualTree.Find<ItemsControl>(pane, "ReminderList");
        return [.. VisualTree.Descendants(list).OfType<Button>()
            .Where(b => b.DataContext is ReminderRowViewModel && b.Content is TextBlock)];
    }

    // The one line of text of a Reminder row.
    private static TextBlock RowText(Button row) => (TextBlock)row.Content;

    // Invokes a control that opens the Reminder flyout and waits for the flyout's editor to show.
    private async Task<(Flyout Flyout, FrameworkElement Editor)> OpenReminderFlyoutAsync(DetailPane pane, Button opener)
    {
        var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(VisualTree.Find<StackPanel>(pane, "ReminderSection"));
        opener.Focus(FocusState.Programmatic);
        Invoke(opener);
        var editor = (FrameworkElement)flyout.Content;
        await UiWait.UntilAsync(() => flyout.IsOpen && editor.IsLoaded && editor.ActualHeight > 0,
            () => "The Reminder flyout did not open.", Ct);
        return (flyout, editor);
    }

    [UITestMethod]
    public async Task A_snoozed_Reminder_shows_its_Snooze_and_Clear_Snooze_clears_it()
    {
        using var db = await DatabaseAsync();
        var id = await AddAsync(db, "Call the bank");
        var reminders = db.Get<IReminderService>();
        await reminders.AddReminderAsync(id, ReminderValue.Fixed(new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc)), Ct);
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
            HorizontalSpacing = 4,
            VerticalSpacing = 4,
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
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
