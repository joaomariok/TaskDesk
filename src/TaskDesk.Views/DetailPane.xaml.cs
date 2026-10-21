using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using Windows.System;

namespace TaskDesk.Views;

/// <summary>
/// The detail pane of the Task the view model shows (<see cref="MainViewModel.DetailTask"/>): its title and
/// completion, Subtasks, Due, Reminders, Repeat, Priority, List, Tags and Notes. Edits save through the view
/// model. <see cref="DetailDock"/> shows it beside or over the list.
/// </summary>
public sealed partial class DetailPane : UserControl
{
    public DetailPane(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        Repeat = viewModel.Repeat;
        ReminderEditor = viewModel.ReminderEditor;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ReminderEditor.PropertyChanged += OnReminderEditorChanged;
            ViewModel.ReminderRowsRebuilding += OnReminderRowsRebuilding;
            ViewModel.ReminderRowsRebuilt += OnReminderRowsRebuilt;
        };
        Unloaded += (_, _) =>
        {
            ReminderEditor.PropertyChanged -= OnReminderEditorChanged;
            ViewModel.ReminderRowsRebuilding -= OnReminderRowsRebuilding;
            ViewModel.ReminderRowsRebuilt -= OnReminderRowsRebuilt;
        };
    }

    public MainViewModel ViewModel { get; }

    /// <summary>The Repeat editor of the view model, which never changes.</summary>
    public RepeatEditorViewModel Repeat { get; }

    /// <summary>The Reminder flyout's editor, which never changes.</summary>
    public ReminderEditorViewModel ReminderEditor { get; }

    /// <summary>The Repeat choices, in the order of <see cref="RepeatPreset"/>'s values.</summary>
    public static string[] RepeatNames { get; } = Enum.GetNames<RepeatPreset>();

    /// <summary>Ticks or un-ticks a Subtask; its checkbox only shows the result.</summary>
    public static ICommand ToggleSubtaskCommand { get; } = new RelayCommand<SubtaskRowViewModel>(subtask =>
    {
        if (subtask is not null)
        {
            subtask.IsCompleted = !subtask.IsCompleted;
        }
    });

    /// <summary>
    /// Runs the pane's shortcut for a key: Enter in the new Subtask box adds the Subtask, Enter in the new Tag box
    /// adds the Tag. Returns false when the key is not a shortcut or there is nothing to do.
    /// </summary>
    public bool TryShortcut(VirtualKey key, VirtualKeyModifiers modifiers) => (key, modifiers) switch
    {
        (VirtualKey.Enter, VirtualKeyModifiers.None) when HasFocus(NewSubtaskBox) => Run(ViewModel.AddSubtaskCommand),
        (VirtualKey.Enter, VirtualKeyModifiers.None) when HasFocus(NewTagBox) => Run(ViewModel.AddTagCommand),
        _ => false,
    };

    private static bool Run(ICommand command)
    {
        if (!command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    private bool HasFocus(Control control) =>
        XamlRoot is { } root && ReferenceEquals(FocusManager.GetFocusedElement(root), control);

    private void OnAddBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && TryShortcut(e.Key, VirtualKeyModifiers.None))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Raised by the Close button. <see cref="DetailDock"/> closes the pane as Esc does, returning focus to the
    /// Task's row, which a collapsing pane would otherwise hand to the next focusable element.
    /// </summary>
    public event EventHandler? CloseRequested;

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnMoveSubtaskUpClick(object sender, RoutedEventArgs e) =>
        RunOnItem<SubtaskRowViewModel>(sender, ViewModel.MoveSubtaskUpCommand);

    private void OnMoveSubtaskDownClick(object sender, RoutedEventArgs e) =>
        RunOnItem<SubtaskRowViewModel>(sender, ViewModel.MoveSubtaskDownCommand);

    private void OnDeleteSubtaskClick(object sender, RoutedEventArgs e) =>
        RunOnItem<SubtaskRowViewModel>(sender, ViewModel.DeleteSubtaskCommand);

    private void OnRemoveTagClick(object sender, RoutedEventArgs e) =>
        RunOnItem<Tag>(sender, ViewModel.RemoveTagCommand);

    // A button in an item template acts on its item, the template's data context.
    private static void RunOnItem<T>(object sender, ICommand command) where T : class
    {
        if (sender is FrameworkElement { DataContext: T item } && command.CanExecute(item))
        {
            command.Execute(item);
        }
    }

    /// <summary>The pane reads a row's Subtask progress "2/5" as "2 of 5".</summary>
    public static string ProgressText(string progress) => progress.Replace("/", " of ", StringComparison.Ordinal);

    /// <summary>The accessible name of a Tag chip's remove button: "Remove #name".</summary>
    public static string RemoveTagName(string name) => "Remove #" + name;

    public static string NumberText(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>What "Every N" counts for a Repeat preset.</summary>
    public static string IntervalUnit(RepeatPreset preset) => preset switch
    {
        RepeatPreset.Daily => "days",
        RepeatPreset.Weekly => "weeks",
        RepeatPreset.Monthly => "months",
        RepeatPreset.Yearly => "years",
        _ => "",
    };

    public static int RepeatIndex(RepeatPreset preset) => (int)preset;

    public void SetRepeatIndex(int index)
    {
        if (index >= 0 && Enum.IsDefined((RepeatPreset)index) && ViewModel.Repeat.Preset != (RepeatPreset)index)
        {
            ViewModel.Repeat.Preset = (RepeatPreset)index;
        }
    }

    /// <summary>Takes a typed interval; text that is not a number leaves it as it was.</summary>
    public void SetInterval(string text)
    {
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            ViewModel.Repeat.Interval = value;
        }
    }

    /// <summary>Takes a typed day of the month; text that is not a number leaves it as it was.</summary>
    public void SetDayOfMonth(string text)
    {
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            ViewModel.Repeat.DayOfMonth = value;
        }
    }

    /// <summary>The picked Reminder day in the flyout; clearing the picker clears the time too.</summary>
    public void SetReminderDate(DateTimeOffset? picked)
    {
        var date = ToDay(picked);
        if (ReminderEditor.Date != date)
        {
            ReminderEditor.Date = date;
        }
    }

    // What opened the Reminder flyout, which takes the focus back when it closes: the Reminder of a row opener
    // (its row may be rebuilt, and the old button let go of, before the flyout closes), null for "Add reminder",
    // and how the opener had the focus, so a keyboard user gets the focus visual back.
    private Guid? _reminderOpenerId;
    private FocusState _reminderOpenerFocus;

    private void OnAddReminderClick(object sender, RoutedEventArgs e)
    {
        ViewModel.AddReminderCommand.Execute(null);
        ShowReminderFlyout((Control)sender, reminderId: null);
    }

    private void OnEditReminderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ReminderRowViewModel row })
        {
            row.EditCommand.Execute(null);
            ShowReminderFlyout((Control)sender, row.Id);
        }
    }

    // Each ShowAt counts; the flyout's Closed can arrive after the next ShowAt, and then belongs to an earlier one.
    private int _reminderShows;
    private int _reminderShowOpened;

    private void ShowReminderFlyout(Control opener, Guid? reminderId)
    {
        _reminderOpenerId = reminderId;
        _reminderOpenerFocus = RestorableFocus(opener.FocusState);
        _reminderShows++;
        ReminderFlyout.ShowAt(opener);
    }

    private void OnReminderFlyoutOpened(object? sender, object e) => _reminderShowOpened = _reminderShows;

    // Save and Cancel close the editor; the flyout follows.
    private void OnReminderEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReminderEditorViewModel.IsOpen) && !ReminderEditor.IsOpen && ReminderFlyout.IsOpen)
        {
            ReminderFlyout.Hide();
        }
    }

    // Esc or a click outside closes the flyout without saving; the focus goes back to what opened it, or to
    // "Add reminder" when that row is gone.
    private void OnReminderFlyoutClosed(object? sender, object e)
    {
        if (_reminderShowOpened != _reminderShows)
        {
            return; // shown again since: the editor and the focus belong to that showing
        }

        // The focus goes back before the editor closes: closing it can rebuild the rows (an update that waited for
        // the flyout), and the rebuild then hands the focus from the old row to that Reminder's new one. Save has
        // closed the editor already, so a row opener is found again by its Reminder.
        Control target = _reminderOpenerId is { } id
            ? ReminderRowControl(id, column: 0) ?? AddReminderButton
            : AddReminderButton;
        _reminderOpenerId = null;
        target.Focus(_reminderOpenerFocus);
        ReminderEditor.CancelCommand.Execute(null);
    }

    // The Reminder whose × was pressed, the Task it belongs to and its place in the rows, from the press until the
    // rebuild that drops it, or until its removal ends without one (refused, or nothing stored).
    private (Guid Id, Guid TaskId, int Index)? _removedReminder;

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI event handler; the signature requires void.")]
    private async void OnRemoveReminderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ReminderRowViewModel row } || !row.RemoveCommand.CanExecute(null))
        {
            return;
        }

        var removed = (row.Id, row.TaskId, ViewModel.DetailReminders.IndexOf(row));
        _removedReminder = removed;
        try
        {
            await row.RemoveCommand.ExecuteAsync(null);
        }
        finally
        {
            if (_removedReminder == removed)
            {
                _removedReminder = null;
            }
        }
    }

    // The Reminder whose × was pressed, once a rebuild of its own Task's rows no longer lists it; null otherwise.
    // The record goes once used, and when the pane shows another Task.
    private (Guid Id, Guid TaskId, int Index)? TakeRemovedReminder()
    {
        if (_removedReminder is not { } removed)
        {
            return null;
        }

        if (ViewModel.DetailTask?.Id == removed.TaskId && ViewModel.DetailReminders.Any(r => r.Id == removed.Id))
        {
            return null; // not removed yet
        }

        _removedReminder = null;
        return ViewModel.DetailTask?.Id == removed.TaskId ? removed : null;
    }

    // The Reminder whose row (column 0) or × (column 1) had the focus when the rows began a rebuild, and the Task
    // it belongs to.
    private (Guid Id, Guid TaskId, int Column, FocusState State)? _focusedReminder;

    private void OnReminderRowsRebuilding(object? sender, EventArgs e) =>
        _focusedReminder = XamlRoot is { } root
            && FocusManager.GetFocusedElement(root) is Button { DataContext: ReminderRowViewModel row } button
                ? (row.Id, row.TaskId, Grid.GetColumn(button), button.FocusState)
                : null;

    // A rebuild replaces every row, the focused one too: the focus goes to that Reminder's new row. A Reminder
    // removed with its × hands the focus to the row now in its place, else the previous row, else "Add reminder".
    // Gone from the same Task any other way (a cleared Due drops a before-Due row), it goes to "Add reminder";
    // when the pane shows another Task, the focus stays where XAML puts it, never on that Task's rows.
    private void OnReminderRowsRebuilt(object? sender, EventArgs e)
    {
        var removed = TakeRemovedReminder();
        if (_focusedReminder is not { } focused)
        {
            return;
        }

        _focusedReminder = null;
        if (ViewModel.DetailTask?.Id != focused.TaskId)
        {
            return;
        }

        Control? target = ReminderRowControl(focused.Id, focused.Column);
        if (target is null)
        {
            var rows = ViewModel.DetailReminders;
            target = removed?.Id == focused.Id && rows.Count > 0
                ? ReminderRowControl(rows[Math.Min(removed.Value.Index, rows.Count - 1)].Id, focused.Column)
                : null;
            target ??= AddReminderButton;
        }

        target.Focus(RestorableFocus(focused.State));
    }

    // A control's focus state to hand on: Unfocused (a control that lost the focus already) becomes Programmatic.
    private static FocusState RestorableFocus(FocusState state) =>
        state == FocusState.Unfocused ? FocusState.Programmatic : state;

    // The row button (column 0) or × (column 1) of a listed Reminder; null when it has no row.
    private Button? ReminderRowControl(Guid id, int column)
    {
        if (ViewModel.DetailReminders.FirstOrDefault(r => r.Id == id) is not { } row)
        {
            return null;
        }

        ReminderList.UpdateLayout(); // realizes the new rows' containers
        return ReminderList.ContainerFromItem(row) is { } container
            ? VisualTreeWalk.Descendants(container).OfType<Button>().FirstOrDefault(b => Grid.GetColumn(b) == column)
            : null;
    }

    /// <summary>The picked Repeat end day; clearing the picker removes the end.</summary>
    public void SetRepeatEndDate(DateTimeOffset? picked)
    {
        var date = ToDay(picked);
        if (ViewModel.Repeat.EndDate != date)
        {
            ViewModel.Repeat.EndDate = date;
        }
    }

    // The calendar day a picker shows, in the local time zone.
    private static DateTime? ToDay(DateTimeOffset? picked) => picked is { } value ? value.ToLocalTime().Date : null;

    /// <summary>The Priority choices, in the order of <see cref="Priority"/>'s values.</summary>
    public static string[] PriorityNames { get; } = Enum.GetNames<Priority>();

    // A UserControl has no automation peer, so UI Automation would skip the pane and its AutomationId (DetailPane)
    // and show only its fields.
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    /// <summary>Focuses the title box with its text selected (F2, or Enter on a row).</summary>
    public void FocusTitle()
    {
        DetailTitleBox.Focus(FocusState.Keyboard);
        DetailTitleBox.SelectAll();
    }

    /// <summary>
    /// An error text takes room only while it has text. x:Bind never calls a function whose argument is null, so
    /// each binding's <c>FallbackValue=Collapsed</c> hides a cleared (null) error.
    /// </summary>
    public static Visibility ShownWithText(string? text) =>
        string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>A past Reminder row is dimmed like a completed Task's row.</summary>
    public static double ReminderOpacity(bool isPast) => TaskRow.RowOpacity(isPast, keepsFullStrength: false);

    /// <summary>A past Reminder row is struck through like a completed Task's title.</summary>
    public static Windows.UI.Text.TextDecorations ReminderDecorations(bool isPast) =>
        TaskRow.TitleDecorations(isPast, keepsFullStrength: false);

    /// <summary>A List without a colour shows a hollow ring instead of a dot.</summary>
    public static Visibility ShownWithoutColour(bool hasListColor) =>
        hasListColor ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// The Due date as the calendar picker holds a day: midnight in the local time zone, which is the day it
    /// shows. x:Bind never calls a function whose argument is null, so each binding's <c>FallbackValue={x:Null}</c>
    /// empties the picker for a Task without that date.
    /// </summary>
    public static DateTimeOffset? ToPickerDate(DateTime? date) =>
        date is { } day ? new DateTimeOffset(DateTime.SpecifyKind(day.Date, DateTimeKind.Local)) : null;

    /// <summary>
    /// The picked calendar day (the day the picker shows, in the local time zone) back into the view model;
    /// clearing the picker clears the Due date.
    /// </summary>
    public void SetDueDate(DateTimeOffset? picked)
    {
        DateTime? date = picked is { } value ? value.ToLocalTime().Date : null;
        if (ViewModel.DetailDueDate != date)
        {
            ViewModel.DetailDueDate = date;
        }
    }

    public static int PriorityIndex(Priority priority) => (int)priority;

    public void SetPriorityIndex(int index)
    {
        if (index >= 0 && Enum.IsDefined((Priority)index) && ViewModel.DetailPriority != (Priority)index)
        {
            ViewModel.DetailPriority = (Priority)index;
        }
    }

    // The Nag choices: Off, then each period in the order of NagPeriod's values.
    private static readonly NagPeriod?[] NagChoices =
        [null, NagPeriod.FifteenMinutes, NagPeriod.ThirtyMinutes, NagPeriod.OneHour, NagPeriod.ThreeHours, NagPeriod.OneDay];

    /// <summary>The Nag choices' labels, Off first.</summary>
    public static string[] NagNames { get; } =
        ["Off", "Every 15 min", "Every 30 min", "Every hour", "Every 3 hours", "Every day"];

    /// <summary>
    /// The index of a Nag period among the choices; Off (0) for none. x:Bind never calls a function whose argument
    /// is null, so the binding's <c>FallbackValue=0</c> shows Off.
    /// </summary>
    public static int NagIndex(NagPeriod? period) => Array.IndexOf(NagChoices, period);

    public void SetNagIndex(int index)
    {
        if (index >= 0 && index < NagChoices.Length && ViewModel.DetailNagPeriod != NagChoices[index])
        {
            ViewModel.DetailNagPeriod = NagChoices[index];
        }
    }
}
