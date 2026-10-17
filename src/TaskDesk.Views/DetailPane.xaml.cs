using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;

namespace TaskDesk.Views;

/// <summary>
/// The detail pane of the Task the view model shows (<see cref="MainViewModel.DetailTask"/>): its title and
/// completion, Due, Priority, List and Notes. Edits save through the view model. <see cref="DetailDock"/>
/// shows it beside or over the list.
/// </summary>
public sealed partial class DetailPane : UserControl
{
    public DetailPane(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    /// <summary>The Priority choices, in the order of <see cref="Priority"/>'s values.</summary>
    public static string[] PriorityNames { get; } = Enum.GetNames<Priority>();

    /// <summary>Focuses the title box with its text selected (F2, or Enter on a row).</summary>
    public void FocusTitle()
    {
        DetailTitleBox.Focus(FocusState.Keyboard);
        DetailTitleBox.SelectAll();
    }

    /// <summary>An error text takes room only while it has text.</summary>
    public static Visibility ShownWithText(string? text) =>
        string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>A List without a colour shows a hollow ring instead of a dot.</summary>
    public static Visibility ShownWithoutColour(bool hasListColor) =>
        hasListColor ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// The Due date as the calendar picker holds a day: midnight in the local time zone, which is the day it
    /// shows.
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
}
