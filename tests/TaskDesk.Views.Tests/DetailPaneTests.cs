using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using Windows.Foundation;
using Windows.System;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The detail pane over a real database: it opens on a row, shows and saves the title, completion, Due,
/// Priority, List and Notes, places each error, docks beside the list above 1100 px and overlays it at and
/// below, and its splitter width is measured for saving and restored.
/// </summary>
[TestClass]
public sealed class DetailPaneTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task Selecting_a_Task_in_a_wide_window_docks_the_pane_beside_the_list_with_its_fields()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist", notes: "Ask about Friday", priority: Priority.High,
            due: new DateOnly(2030, 10, 25));
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            var list = await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;
            await UiWait.UntilAsync(() => pane.Visibility == Visibility.Visible && pane.ActualWidth > 0,
                () => "Selecting the Task did not show the docked pane.", Ct);

            Assert.AreEqual("DetailPane", AutomationProperties.GetAutomationId(pane));
            Assert.AreEqual("Book dentist", Box(pane, "DetailTitleBox").Text);
            Assert.AreEqual("Ask about Friday", Box(pane, "DetailNotesBox").Text);
            Assert.AreEqual("High", (string)VisualTree.Find<ComboBox>(pane, "PriorityBox").SelectedItem);
            var picker = VisualTree.Find<CalendarDatePicker>(pane, "DetailDueDatePicker");
            Assert.AreEqual(new DateTime(2030, 10, 25), picker.Date?.ToLocalTime().Date);
            Assert.IsTrue(Left(pane, window) >= Right(list, window) - 0.5, "The docked pane overlaps the list.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_pane_keeps_the_automation_ids_and_names_the_UI_tests_use()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, _) = await OpenAsync(db, width: 1200);
        try
        {
            await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;
            await UiWait.UntilAsync(() => pane.ActualWidth > 0, () => "The pane did not show.", Ct);

            // UI Automation sees the pane itself (a UserControl has no peer of its own), as the UI tests find it.
            var panePeer = FrameworkElementAutomationPeer.CreatePeerForElement(pane);
            Assert.IsNotNull(panePeer, "UI Automation does not see the detail pane.");
            Assert.AreEqual("DetailPane", panePeer.GetAutomationId());
            var check = VisualTree.Find<CheckBox>(pane, "DetailCompletedBox");
            Assert.AreEqual("DetailCompletedBox", AutomationProperties.GetAutomationId(check));
            Assert.AreEqual("Completed", AutomationProperties.GetName(check));
            Assert.IsInstanceOfType<IToggleProvider>(FrameworkElementAutomationPeer.CreatePeerForElement(check));
            AssertIds(pane, "DetailTitleBox", "Task title");
            AssertIds(pane, "DetailNotesBox", "Task notes");
            AssertIds(pane, "DetailDueDatePicker", "Due date");
            AssertIds(pane, "DueTimePicker", "Due time");
            AssertIds(pane, "ClearDueTimeButton", "Clear Due time");
            AssertIds(pane, "ReminderTimePicker", "Reminder time");
            AssertIds(pane, "ClearReminderTimeButton", "Clear Reminder time");
            var splitter = VisualTree.Find<GridSplitter>(window.Details, "DetailSplitter");
            Assert.AreEqual("DetailSplitter", AutomationProperties.GetAutomationId(splitter));
            Assert.AreEqual("Resize detail pane", AutomationProperties.GetName(splitter));
            Assert.AreEqual(Visibility.Visible, splitter.Visibility);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Each_field_edits_and_saves_the_Task()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;
            await UiWait.UntilAsync(() => pane.ActualWidth > 0, () => "The pane did not show.", Ct);

            Box(pane, "DetailTitleBox").Text = "Book the dentist";
            Box(pane, "DetailNotesBox").Text = "Ask about Friday";
            VisualTree.Find<ComboBox>(pane, "PriorityBox").SelectedIndex = (int)Priority.Medium;
            VisualTree.Find<CalendarDatePicker>(pane, "DetailDueDatePicker").Date =
                new DateTimeOffset(new DateTime(2030, 10, 25, 9, 0, 0, DateTimeKind.Local));
            var time = VisualTree.Find<TimePicker>(pane, "DueTimePicker");
            await UiWait.UntilAsync(() => time.IsEnabled, () => "The Due time picker stayed disabled with a Due date.", Ct);
            time.SelectedTime = new TimeSpan(14, 30, 0);
            await UiWait.UntilAsync(() => viewModel.DetailDueTime == new TimeSpan(14, 30, 0),
                () => "The Due time was not taken.", Ct);
            await viewModel.FlushPendingSaveAsync();

            var saved = (await db.QueryAsync(c => c.Tasks)).Single();
            Assert.AreEqual("Book the dentist", saved.Title);
            Assert.AreEqual("Ask about Friday", saved.Notes);
            Assert.AreEqual(Priority.Medium, saved.Priority);
            Assert.AreEqual(new DateOnly(2030, 10, 25), saved.DueDate);
            Assert.AreEqual(new TimeOnly(14, 30), saved.DueTime);

            window.View.TaskList.AnimationsEnabled = () => false;
            ((IToggleProvider)FrameworkElementAutomationPeer.CreatePeerForElement(
                VisualTree.Find<CheckBox>(pane, "DetailCompletedBox"))).Toggle();
            await UiWait.UntilAsync(() => viewModel.DetailTask?.IsCompleted == true,
                () => "The pane's checkbox did not complete the Task.", Ct);
            await viewModel.CompletionMove;
            Assert.IsTrue((await db.QueryAsync(c => c.Tasks)).Single().IsCompleted);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Clearing_the_Due_date_picker_clears_the_Due_date()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist", due: new DateOnly(2030, 10, 25));
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            await SelectAsync(window, "Book dentist");
            var picker = VisualTree.Find<CalendarDatePicker>(window.Details.Pane, "DetailDueDatePicker");

            picker.Date = null;
            await viewModel.FlushPendingSaveAsync();

            Assert.IsNull(viewModel.DetailDueDate);
            Assert.IsNull((await db.QueryAsync(c => c.Tasks)).Single().DueDate);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_date_pickers_show_each_selected_Tasks_own_dates_and_none_it_lacks()
    {
        using var db = await DatabaseAsync();
        var reminder = new DateTime(2030, 10, 24, 9, 0, 0, DateTimeKind.Local).ToUniversalTime();
        await AddAsync(db, "Dated", due: new DateOnly(2030, 10, 25), reminderUtc: reminder);
        await AddAsync(db, "Plain");
        await AddAsync(db, "Due only", due: new DateOnly(2030, 11, 2));
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            await SelectAsync(window, "Dated");
            var pane = window.Details.Pane;
            await UiWait.UntilAsync(() => pane.ActualWidth > 0, () => "The pane did not show.", Ct);
            var due = VisualTree.Find<CalendarDatePicker>(pane, "DetailDueDatePicker");
            var remind = VisualTree.Find<CalendarDatePicker>(pane, "ReminderDatePicker");
            DateTime? Day(CalendarDatePicker picker) => picker.Date?.ToLocalTime().Date;

            await UiWait.UntilAsync(() => Day(due) == new DateTime(2030, 10, 25) && Day(remind) == new DateTime(2030, 10, 24),
                () => $"The dated Task shows Due {Day(due)} and Reminder {Day(remind)}.", Ct);

            await SelectAsync(window, "Plain");
            await UiWait.UntilAsync(() => due.Date is null && remind.Date is null,
                () => $"A Task without dates shows Due {Day(due)} and Reminder {Day(remind)}.", Ct);

            await SelectAsync(window, "Dated");
            await UiWait.UntilAsync(() => Day(remind) == new DateTime(2030, 10, 24), () => "The Reminder did not show again.", Ct);
            await SelectAsync(window, "Due only");
            await UiWait.UntilAsync(() => Day(due) == new DateTime(2030, 11, 2) && remind.Date is null,
                () => $"A Task with only a Due date shows Due {Day(due)} and Reminder {Day(remind)}.", Ct);

            await viewModel.FlushPendingSaveAsync();
            var saved = (await db.QueryAsync(c => c.Tasks)).ToDictionary(t => t.Title);
            Assert.AreEqual(new DateOnly(2030, 10, 25), saved["Dated"].DueDate, "Moving between Tasks changed a Due date.");
            Assert.AreEqual(reminder, saved["Dated"].ReminderUtc, "Moving between Tasks changed a Reminder.");
            Assert.IsNull(saved["Plain"].DueDate);
            Assert.IsNull(saved["Plain"].ReminderUtc);
            Assert.IsNull(saved["Due only"].ReminderUtc);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_cleared_field_error_takes_no_room()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist", due: new DateOnly(2030, 10, 25));
        var (window, viewModel) = await OpenAsync(db, width: 1200, height: 1000);
        try
        {
            await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;
            await UiWait.UntilAsync(() => pane.ActualWidth > 0, () => "The pane did not show.", Ct);

            // Each error shows, then clears; a cleared error takes no room.
            await ShowThenClearAsync(pane, "DueErrorText",
                () =>
                {
                    viewModel.DetailDueDate = null;
                    viewModel.DetailDueTime = new TimeSpan(14, 30, 0);
                },
                () =>
                {
                    viewModel.DetailDueTime = null;
                    viewModel.DetailDueDate = new DateTime(2030, 10, 25);
                });
            await ShowThenClearAsync(pane, "ReminderErrorText",
                () => viewModel.DetailReminderTime = new TimeSpan(14, 30, 0), () => viewModel.DetailReminderTime = null);
            await ShowThenClearAsync(pane, "RepeatErrorText",
                () =>
                {
                    viewModel.Repeat.Preset = RepeatPreset.Daily;
                    viewModel.Repeat.Interval = 0;
                },
                () => viewModel.Repeat.Interval = 1);
            await ShowThenClearAsync(pane, "GeneralErrorText",
                () => viewModel.ErrorMessage = "Could not save the changes: disk full", () => viewModel.ErrorMessage = null);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_Close_button_closes_the_pane_and_returns_focus_to_the_Tasks_row()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;
            await UiWait.UntilAsync(() => pane.ActualWidth > 0, () => "The pane did not show.", Ct);
            var close = VisualTree.Descendants(pane).OfType<Button>()
                .Single(b => AutomationProperties.GetName(b) == "Close detail pane");
            close.Focus(FocusState.Keyboard);

            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(close)).Invoke();

            await UiWait.UntilAsync(
                () => !viewModel.IsDetailPaneVisible
                      && FocusManager.GetFocusedElement(window.Content.XamlRoot) is ListViewItem item
                      && AutomationProperties.GetName(item) == "Book dentist",
                () => $"Close left the pane open or focus on {FocusManager.GetFocusedElement(window.Content.XamlRoot)}.", Ct);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_Due_time_error_shows_right_under_the_Due_field_above_the_Priority_label()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, viewModel) = await OpenAsync(db, width: 1200, height: 1000);
        try
        {
            await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;

            viewModel.DetailDueTime = new TimeSpan(14, 30, 0);
            pane.UpdateLayout();

            var error = ShownText(pane, "A Due time needs a Due date.");
            var picker = VisualTree.Find<TimePicker>(pane, "DueTimePicker");
            Assert.IsTrue(Top(error, pane) >= Bottom(picker, pane) - 0.5, "The error is above the Due time field.");
            Assert.IsTrue(Bottom(error, pane) <= Top(Label(pane, "Priority"), pane) + 0.5, "The error is below the Priority label.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_general_error_shows_under_the_header_above_the_fields()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;

            viewModel.ErrorMessage = "Could not save the changes: disk full";
            pane.UpdateLayout();

            var error = ShownText(pane, "Could not save the changes: disk full");
            Assert.IsTrue(Top(error, pane) >= Bottom(Box(pane, "DetailTitleBox"), pane) - 0.5);
            Assert.IsTrue(Bottom(error, pane) <= Top(VisualTree.Find<ScrollViewer>(pane, "Fields"), pane) + 0.5,
                "The general error is not in the fixed header.");
            Assert.IsFalse(VisualTree.Descendants(pane).OfType<TextBlock>()
                .Any(t => t.Name == "DueErrorText" && t.Visibility == Visibility.Visible), "A general error showed under Due.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_Due_time_picker_has_a_visible_label_steps_by_the_minute_and_needs_a_Due_date()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;
            await UiWait.UntilAsync(() => pane.ActualWidth > 0, () => "The pane did not show.", Ct);
            var time = VisualTree.Find<TimePicker>(pane, "DueTimePicker");
            var clear = VisualTree.Find<Button>(pane, "ClearDueTimeButton");

            Assert.IsTrue(Label(pane, "Due time").ActualHeight > 0, "The Due time has no visible label.");
            Assert.AreEqual(1, time.MinuteIncrement);
            Assert.IsNull(time.SelectedTime);
            Assert.IsFalse(time.IsEnabled, "The Due time picker is enabled without a Due date.");
            Assert.IsFalse(clear.IsEnabled, "The clear button is enabled without a Due time.");

            VisualTree.Find<CalendarDatePicker>(pane, "DetailDueDatePicker").Date =
                new DateTimeOffset(new DateTime(2030, 10, 25, 0, 0, 0, DateTimeKind.Local));
            await UiWait.UntilAsync(() => time.IsEnabled, () => "Picking a Due date did not enable the Due time.", Ct);
            Assert.IsFalse(clear.IsEnabled, "The clear button is enabled for a date-only Due.");
            await viewModel.FlushPendingSaveAsync();
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_Due_time_picker_shows_each_Tasks_saved_time_to_the_minute()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Timed", due: new DateOnly(2030, 10, 25), dueTime: new TimeOnly(14, 7));
        await AddAsync(db, "Date only", due: new DateOnly(2030, 10, 26));
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            await SelectAsync(window, "Timed");
            var pane = window.Details.Pane;
            await UiWait.UntilAsync(() => pane.ActualWidth > 0, () => "The pane did not show.", Ct);
            var time = VisualTree.Find<TimePicker>(pane, "DueTimePicker");
            await UiWait.UntilAsync(() => time.SelectedTime == new TimeSpan(14, 7, 0),
                () => $"The timed Task shows {time.SelectedTime}.", Ct);

            await SelectAsync(window, "Date only");
            await UiWait.UntilAsync(() => time.SelectedTime is null,
                () => $"The date-only Task shows {time.SelectedTime}.", Ct);

            await SelectAsync(window, "Timed");
            await UiWait.UntilAsync(() => time.SelectedTime == new TimeSpan(14, 7, 0),
                () => $"The timed Task shows {time.SelectedTime} again.", Ct);

            await viewModel.FlushPendingSaveAsync();
            var saved = (await db.QueryAsync(c => c.Tasks)).ToDictionary(t => t.Title);
            Assert.AreEqual(new TimeOnly(14, 7), saved["Timed"].DueTime, "Moving between Tasks changed a Due time.");
            Assert.IsNull(saved["Date only"].DueTime);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Picking_a_Due_time_saves_it_and_the_clear_button_makes_the_Due_date_only()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist", due: new DateOnly(2030, 10, 25));
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;
            await UiWait.UntilAsync(() => pane.ActualWidth > 0, () => "The pane did not show.", Ct);
            var time = VisualTree.Find<TimePicker>(pane, "DueTimePicker");
            var clear = VisualTree.Find<Button>(pane, "ClearDueTimeButton");

            time.SelectedTime = new TimeSpan(14, 7, 0);
            await UiWait.UntilAsync(() => clear.IsEnabled, () => "Picking a Due time did not enable its clear button.", Ct);
            await viewModel.FlushPendingSaveAsync();
            Assert.AreEqual(new TimeOnly(14, 7), (await db.QueryAsync(c => c.Tasks)).Single().DueTime);

            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(clear)).Invoke();
            await UiWait.UntilAsync(() => time.SelectedTime is null, () => "The clear button left the picker set.", Ct);
            await viewModel.FlushPendingSaveAsync();

            var saved = (await db.QueryAsync(c => c.Tasks)).Single();
            Assert.AreEqual(new DateOnly(2030, 10, 25), saved.DueDate);
            Assert.IsNull(saved.DueTime);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Clearing_the_Due_date_picker_clears_the_Due_time_picker()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist", due: new DateOnly(2030, 10, 25), dueTime: new TimeOnly(14, 30));
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;
            var time = VisualTree.Find<TimePicker>(pane, "DueTimePicker");
            await UiWait.UntilAsync(() => time.SelectedTime == new TimeSpan(14, 30, 0),
                () => $"The picker shows {time.SelectedTime}.", Ct);

            VisualTree.Find<CalendarDatePicker>(pane, "DetailDueDatePicker").Date = null;
            await UiWait.UntilAsync(() => time.SelectedTime is null && !time.IsEnabled,
                () => $"Clearing the Due date left the time picker at {time.SelectedTime} ({time.IsEnabled}).", Ct);
            await viewModel.FlushPendingSaveAsync();

            var saved = (await db.QueryAsync(c => c.Tasks)).Single();
            Assert.IsNull(saved.DueDate);
            Assert.IsNull(saved.DueTime);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_Reminder_time_picker_has_a_visible_label_steps_by_the_minute_and_needs_a_Reminder_date()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;
            await UiWait.UntilAsync(() => pane.ActualWidth > 0, () => "The pane did not show.", Ct);
            var time = VisualTree.Find<TimePicker>(pane, "ReminderTimePicker");
            var clear = VisualTree.Find<Button>(pane, "ClearReminderTimeButton");

            Assert.IsTrue(Label(pane, "Reminder time").ActualHeight > 0, "The Reminder time has no visible label.");
            Assert.AreEqual(1, time.MinuteIncrement);
            Assert.IsNull(time.SelectedTime);
            Assert.IsFalse(time.IsEnabled, "The Reminder time picker is enabled without a Reminder date.");
            Assert.IsFalse(clear.IsEnabled, "The clear button is enabled without a Reminder time.");
            await viewModel.FlushPendingSaveAsync();
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_date_only_Reminder_shows_the_default_time_and_clearing_the_time_falls_back_to_it()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;
            await UiWait.UntilAsync(() => pane.ActualWidth > 0, () => "The pane did not show.", Ct);
            var time = VisualTree.Find<TimePicker>(pane, "ReminderTimePicker");
            var clear = VisualTree.Find<Button>(pane, "ClearReminderTimeButton");

            VisualTree.Find<CalendarDatePicker>(pane, "ReminderDatePicker").Date =
                new DateTimeOffset(new DateTime(2030, 10, 25, 0, 0, 0, DateTimeKind.Local));
            await UiWait.UntilAsync(() => time.SelectedTime == new TimeSpan(9, 0, 0),
                () => $"A date-only Reminder shows {time.SelectedTime}.", Ct);
            await viewModel.FlushPendingSaveAsync();

            time.SelectedTime = new TimeSpan(14, 7, 0);
            await UiWait.UntilAsync(() => viewModel.DetailReminderTime == new TimeSpan(14, 7, 0),
                () => "The Reminder time was not taken.", Ct);
            await viewModel.FlushPendingSaveAsync();
            var picked = (await db.QueryAsync(c => c.Tasks)).Single().ReminderUtc;
            Assert.AreEqual(new DateTime(2030, 10, 25, 14, 7, 0, DateTimeKind.Utc),
                picked);

            Assert.IsTrue(clear.IsEnabled, "The clear button is disabled with a Reminder time.");
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(clear)).Invoke();
            await UiWait.UntilAsync(() => time.SelectedTime == new TimeSpan(9, 0, 0),
                () => $"Clearing the time left the picker at {time.SelectedTime}.", Ct);
            await viewModel.FlushPendingSaveAsync();

            var saved = (await db.QueryAsync(c => c.Tasks)).Single().ReminderUtc;
            Assert.IsNotNull(saved, "Clearing the time cleared the whole Reminder.");
            Assert.AreEqual(new DateTime(2030, 10, 25, 9, 0, 0, DateTimeKind.Utc),
                saved);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Clearing_the_Reminder_date_picker_clears_the_whole_Reminder()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;
            await UiWait.UntilAsync(() => pane.ActualWidth > 0, () => "The pane did not show.", Ct);
            var time = VisualTree.Find<TimePicker>(pane, "ReminderTimePicker");
            var date = VisualTree.Find<CalendarDatePicker>(pane, "ReminderDatePicker");

            date.Date = new DateTimeOffset(new DateTime(2030, 10, 25, 0, 0, 0, DateTimeKind.Local));
            await UiWait.UntilAsync(() => time.IsEnabled, () => "Picking a date did not enable the time.", Ct);
            time.SelectedTime = new TimeSpan(14, 30, 0);
            await UiWait.UntilAsync(() => viewModel.DetailReminderTime == new TimeSpan(14, 30, 0),
                () => "The Reminder time was not taken.", Ct);
            await viewModel.FlushPendingSaveAsync();

            date.Date = null;
            await UiWait.UntilAsync(() => time.SelectedTime is null && !time.IsEnabled,
                () => $"Clearing the Reminder date left the time at {time.SelectedTime} ({time.IsEnabled}).", Ct);
            await viewModel.FlushPendingSaveAsync();

            Assert.IsNull((await db.QueryAsync(c => c.Tasks)).Single().ReminderUtc);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_List_shows_as_text_not_as_a_box()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, _) = await OpenAsync(db, width: 1200);
        try
        {
            await SelectAsync(window, "Book dentist");
            var list = VisualTree.Descendants(window.Details.Pane).OfType<FrameworkElement>()
                .Single(e => AutomationProperties.GetName(e) == "List");

            Assert.IsNotInstanceOfType<TextBox>(list);
            Assert.IsFalse(list is Control, "The List is an input.");
            CollectionAssert.Contains(VisualTree.Descendants(list).OfType<TextBlock>().Select(t => t.Text).ToList(),
                "Inbox");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_List_dot_shows_each_selected_Tasks_own_List_colour_and_none_for_an_uncoloured_List()
    {
        using var db = await DatabaseAsync();
        var lists = db.Get<IListService>();
        var home = await lists.CreateListAsync("Home", Ct);
        await lists.SetListColorAsync(home.Id, "#107C10", Ct);
        await AddAsync(db, "Water plants", listId: home.Id);
        await AddAsync(db, "Book dentist");
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            await viewModel.ShowSmartViewAsync(SmartView.All, Ct);
            await SelectAsync(window, "Water plants");
            var pane = window.Details.Pane;
            await UiWait.UntilAsync(() => pane.ActualWidth > 0, () => "The pane did not show.", Ct);
            var field = VisualTree.Find<StackPanel>(pane, "ListField");
            var dot = VisualTree.Descendants(field).OfType<Microsoft.UI.Xaml.Shapes.Ellipse>().First();
            await UiWait.UntilAsync(() => dot.Fill is SolidColorBrush { Color: { R: 0x10, G: 0x7C, B: 0x10 } },
                () => "The coloured List's dot does not show its colour.", Ct);

            await SelectAsync(window, "Book dentist");
            await UiWait.UntilAsync(() => viewModel.DetailTask?.Title == "Book dentist",
                () => "The pane did not move to the other Task.", Ct);
            pane.UpdateLayout();

            Assert.IsNull(dot.Fill, "The dot keeps the previous Task's List colour.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_hairline_parts_the_fixed_header_from_the_scrolling_fields()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, _) = await OpenAsync(db, width: 1200);
        try
        {
            await SelectAsync(window, "Book dentist");
            var header = VisualTree.Find<Border>(window.Details.Pane, "Header");

            Assert.AreEqual(new Thickness(0, 0, 0, 1), header.BorderThickness);
            Assert.IsNotNull(header.BorderBrush);
            Assert.AreEqual(0, Grid.GetRow(header));
            Assert.AreEqual(1, Grid.GetRow(VisualTree.Find<ScrollViewer>(window.Details.Pane, "Fields")));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_pane_overlays_the_list_at_1100_px_and_docks_beside_it_above()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, viewModel) = await OpenAsync(db, width: 1000);
        try
        {
            // 1100 px is the window's width with its frame, as the WPF window measures it.
            await ResizeWindowAsync(window, 1100);
            var list = await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;
            var splitter = VisualTree.Find<GridSplitter>(window.Details, "DetailSplitter");
            Assert.IsFalse(viewModel.IsDetailDocked, "At 1100 px the pane is docked.");
            Assert.AreEqual(Visibility.Collapsed, pane.Visibility, "Selecting a row opened the overlaid pane.");

            Assert.IsTrue(window.View.TaskList.TryShortcut(VirtualKey.Enter, VirtualKeyModifiers.None));
            await UiWait.UntilAsync(() => pane.ActualWidth > 0, () => "Enter did not open the pane.", Ct);
            Assert.IsTrue(Left(pane, window) < Right(list, window), "At 1100 px the pane does not overlay the list.");
            Assert.AreEqual(PaneLayout.DetailOverlayMinWidth, pane.ActualWidth, 0.5);
            Assert.AreEqual(Visibility.Collapsed, splitter.Visibility, "The overlaid pane shows the splitter.");

            await ResizeClientAsync(window, 1200);

            await UiWait.UntilAsync(() => Left(pane, window) >= Right(list, window) - 0.5,
                () => "Above 1100 px the pane did not dock beside the list.", Ct);
            Assert.IsTrue(viewModel.IsDetailDocked);
            Assert.AreEqual(Visibility.Visible, splitter.Visibility);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task An_overlaid_pane_stays_on_its_Task_while_the_selection_moves()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "First");
        await AddAsync(db, "Second");
        var (window, viewModel) = await OpenAsync(db, width: 1000);
        try
        {
            var list = await SelectAsync(window, "First");
            Assert.IsTrue(window.View.TaskList.TryShortcut(VirtualKey.Enter, VirtualKeyModifiers.None));
            await UiWait.UntilAsync(() => window.Details.Pane.ActualWidth > 0, () => "Enter did not open the pane.", Ct);

            list.SelectedIndex = 1;
            window.Content.UpdateLayout();

            Assert.AreEqual("Second", viewModel.SelectedTask?.Title);
            Assert.AreEqual("First", Box(window.Details.Pane, "DetailTitleBox").Text);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Enter_on_a_row_opens_the_pane_with_focus_in_the_title_and_Esc_closes_it_and_returns_focus_to_the_row()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, viewModel) = await OpenAsync(db, width: 1000);
        try
        {
            await SelectAsync(window, "Book dentist");
            var title = Box(window.Details.Pane, "DetailTitleBox");

            Assert.IsTrue(window.View.TaskList.TryShortcut(VirtualKey.Enter, VirtualKeyModifiers.None));
            await UiWait.UntilAsync(() => ReferenceEquals(FocusManager.GetFocusedElement(window.Content.XamlRoot), title),
                () => "Enter did not move focus to the title.", Ct);
            Assert.IsTrue(viewModel.IsDetailPaneVisible);

            Assert.IsTrue(window.Details.TryShortcut(VirtualKey.Escape, VirtualKeyModifiers.None));
            Assert.IsFalse(viewModel.IsDetailPaneVisible, "Esc did not close the pane.");
            await UiWait.UntilAsync(
                () => FocusManager.GetFocusedElement(window.Content.XamlRoot) is ListViewItem item
                      && AutomationProperties.GetName(item) == "Book dentist",
                () => "Esc did not return focus to the Task's row.", Ct);
            Assert.IsFalse(window.Details.TryShortcut(VirtualKey.Escape, VirtualKeyModifiers.None),
                "Esc with the pane closed did something.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task F2_opens_the_pane_on_the_selected_Task_and_a_docked_pane_closed_with_Esc_stays_closed()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "First");
        await AddAsync(db, "Second");
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            var list = await SelectAsync(window, "First");
            window.Details.TryShortcut(VirtualKey.Escape, VirtualKeyModifiers.None);
            Assert.IsFalse(viewModel.IsDetailPaneVisible);

            list.SelectedIndex = 1;
            Assert.IsFalse(viewModel.IsDetailPaneVisible, "Moving the selection reopened the closed pane.");

            Assert.IsTrue(window.Details.TryShortcut(VirtualKey.F2, VirtualKeyModifiers.None));
            Assert.IsTrue(viewModel.IsDetailPaneVisible);
            Assert.AreEqual("Second", Box(window.Details.Pane, "DetailTitleBox").Text);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_docked_width_is_restored_and_measured_for_saving_and_a_hidden_pane_measures_zero()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            window.SetPaneWidths(new WindowLayout(0, 0, 1200, 700,
                PaneWidths: new Dictionary<string, double> { [PaneLayout.Detail] = 340 }));
            Assert.AreEqual(0, window.MeasurePaneWidths()[PaneLayout.Detail], "A hidden pane measures more than 0.");

            await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;
            await UiWait.UntilAsync(() => Math.Abs(pane.ActualWidth - 340) < 0.5,
                () => $"The docked pane is {pane.ActualWidth} px wide, not 340.", Ct);
            Assert.AreEqual(340, window.MeasurePaneWidths()[PaneLayout.Detail], 0.5);

            window.SetPaneWidths(new WindowLayout(0, 0, 1200, 700,
                PaneWidths: new Dictionary<string, double> { [PaneLayout.Detail] = 5000 }));
            await UiWait.UntilAsync(() => Math.Abs(pane.ActualWidth - PaneLayout.DetailLimits.Max) < 0.5,
                () => $"The pane is {pane.ActualWidth} px wide, not its maximum.", Ct);

            window.SetPaneWidths(null);
            await UiWait.UntilAsync(() => Math.Abs(pane.ActualWidth - PaneLayout.DetailLimits.Default) < 0.5,
                () => $"Without a saved layout the pane is {pane.ActualWidth} px wide.", Ct);
        }
        finally
        {
            window.Close();
        }
    }

    // Shows a field error, then clears it and checks it takes no room.
    private async Task ShowThenClearAsync(FrameworkElement pane, string name, Action show, Action clear)
    {
        var error = VisualTree.Find<TextBlock>(pane, name);
        show();
        await UiWait.UntilAsync(() => error.Text.Length > 0 && error.Visibility == Visibility.Visible && error.ActualHeight > 0,
            () => $"{name} did not show; it reads '{error.Text}' ({error.Visibility}).", Ct);

        clear();
        pane.UpdateLayout();
        // A collapsed element keeps its last ActualHeight; the room it asks of the layout is its DesiredSize.
        await UiWait.UntilAsync(() => error.Visibility == Visibility.Collapsed && error.DesiredSize.Height == 0,
            () => $"{name} still takes {error.DesiredSize.Height} px with '{error.Text}' ({error.Visibility}).", Ct);
    }

    private static TextBox Box(FrameworkElement root, string name) => VisualTree.Find<TextBox>(root, name);

    private static void AssertIds(FrameworkElement root, string id, string name)
    {
        var element = VisualTree.Find<FrameworkElement>(root, id);
        Assert.AreEqual(id, AutomationProperties.GetAutomationId(element));
        Assert.AreEqual(name, AutomationProperties.GetName(element));
    }

    private static TextBlock Label(FrameworkElement root, string text) =>
        VisualTree.Descendants(root).OfType<TextBlock>().Single(t => t.Text == text);

    // The one laid-out TextBlock reading text; a collapsed one has no size.
    private static TextBlock ShownText(FrameworkElement root, string text)
    {
        var shown = VisualTree.Descendants(root).OfType<TextBlock>()
            .Where(t => t.Text == text && t.Visibility == Visibility.Visible && t.ActualHeight > 0).ToList();
        Assert.HasCount(1, shown, $"'{text}' is not shown exactly once.");
        return shown[0];
    }

    private static Point Origin(FrameworkElement element, UIElement root) =>
        element.TransformToVisual(root).TransformPoint(new Point(0, 0));

    private static double Top(FrameworkElement element, UIElement root) => Origin(element, root).Y;

    private static double Bottom(FrameworkElement element, UIElement root) => Top(element, root) + element.ActualHeight;

    private static double Left(FrameworkElement element, MainWindow window) => Origin(element, window.Content).X;

    private static double Right(FrameworkElement element, MainWindow window) =>
        Left(element, window) + element.ActualWidth;

    // Selects a Task's row in the open Task list as a click would, and returns the list.
    private async Task<ListView> SelectAsync(MainWindow window, string title)
    {
        var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
        await UiWait.UntilAsync(
            () => VisualTree.Descendants(list).OfType<ListViewItem>().Any(i => AutomationProperties.GetName(i) == title),
            () => $"No row '{title}'.", Ct);
        list.SelectedItem = window.ViewModel.Tasks.Single(t => t.Title == title);
        window.Content.UpdateLayout();
        return list;
    }

    private async Task<(MainWindow Window, MainViewModel ViewModel)> OpenAsync(
        TempDatabase db, double width, double height = 600)
    {
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock,
            dueService: db.Get<IDueService>(), searchService: db.Get<ITaskSearchService>(),
            reminderService: db.Get<IReminderService>(),
            listService: db.Get<IListService>());
        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await UiWait.UntilAsync(() => window.Content is FrameworkElement { IsLoaded: true },
                () => "The window did not load.", Ct);
            await ResizeClientAsync(window, width, height);
            await viewModel.LoadAsync(Ct);
            return (window, viewModel);
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    // Resizes the window, its frame included, to `width` device-independent pixels, and waits for the layout.
    private async Task ResizeWindowAsync(MainWindow window, double width, double height = 600)
    {
        var content = (FrameworkElement)window.Content;
        var scale = content.XamlRoot.RasterizationScale;
        var physical = WindowLayoutCalculator.ToPhysical(width, scale);
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(physical, WindowLayoutCalculator.ToPhysical(height, scale)));
        await UiWait.UntilAsync(
            () => window.AppWindow.Size.Width == physical && Math.Abs(content.ActualWidth - content.XamlRoot.Size.Width) < 1,
            () => $"The window is {window.AppWindow.Size.Width} px wide, not {physical}.", Ct);
        content.UpdateLayout();
    }

    // Resizes the window so its content is `width` device-independent pixels wide, and waits for the layout.
    private async Task ResizeClientAsync(MainWindow window, double width, double height = 600)
    {
        var content = (FrameworkElement)window.Content;
        var scale = content.XamlRoot.RasterizationScale;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(
            (int)Math.Round(width * scale), (int)Math.Round(height * scale)));
        await UiWait.UntilAsync(() => Math.Abs(content.ActualWidth - width) < 1,
            () => $"The content is {content.ActualWidth} px wide, not {width}.", Ct);
        content.UpdateLayout();
    }

    private async Task<TempDatabase> DatabaseAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return db;
    }

    private static async Task AddAsync(
        TempDatabase db, string title, string notes = "", Priority priority = Priority.None, DateOnly? due = null,
        DateTime? reminderUtc = null, Guid? listId = null, TimeOnly? dueTime = null)
    {
        listId ??= (await db.QueryAsync(c => c.Lists.Where(l => l.IsDefault))).Single().Id;
        var order = (await db.QueryAsync(c => c.Tasks)).Count;
        var now = db.Clock.GetUtcNow().UtcDateTime;
        await using var context = await db.Get<Microsoft.EntityFrameworkCore.IDbContextFactory<TaskDeskDbContext>>()
            .CreateDbContextAsync();
        context.Tasks.Add(new TaskItem
        {
            Id = Guid.CreateVersion7(),
            ListId = listId.Value,
            Title = title,
            Notes = notes,
            Priority = priority,
            DueDate = due,
            DueTime = dueTime,
            ReminderUtc = reminderUtc,
            SortOrder = order,
            CreatedUtc = now,
            UpdatedUtc = now,
        });
        await context.SaveChangesAsync();
    }
}
