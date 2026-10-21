using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TaskDesk.Core.Tasks;
using TaskDesk.Presentation;
using Windows.UI.Text;

namespace TaskDesk.Views;

/// <summary>One Task row: what a Task list shows for each <see cref="TaskRowViewModel"/>.</summary>
public sealed partial class TaskRow : UserControl
{
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
        nameof(Row), typeof(TaskRowViewModel), typeof(TaskRow), new PropertyMetadata(null));

    public static readonly DependencyProperty ToggleCompleteCommandProperty = DependencyProperty.Register(
        nameof(ToggleCompleteCommand), typeof(ICommand), typeof(TaskRow), new PropertyMetadata(null));

    public static readonly DependencyProperty ToggleOpenCommandProperty = DependencyProperty.Register(
        nameof(ToggleOpenCommand), typeof(ICommand), typeof(TaskRow), new PropertyMetadata(null));

    public TaskRow()
    {
        InitializeComponent();
    }

    /// <summary>What the Subtasks chevron runs, with the row as its parameter: opening or closing the Task.</summary>
    public ICommand? ToggleOpenCommand
    {
        get => (ICommand?)GetValue(ToggleOpenCommandProperty);
        set => SetValue(ToggleOpenCommandProperty, value);
    }

    /// <summary>The Task the row shows.</summary>
    public TaskRowViewModel? Row
    {
        get => (TaskRowViewModel?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    /// <summary>What the Completed checkbox runs, with the row as its parameter.</summary>
    public ICommand? ToggleCompleteCommand
    {
        get => (ICommand?)GetValue(ToggleCompleteCommandProperty);
        set => SetValue(ToggleCompleteCommandProperty, value);
    }

    /// <summary>The List's colour ("#RRGGBB") as a brush; null (no fill) when the List has none.</summary>
    public static Brush? StripeBrush(string? hex) =>
        ListColour.TryParse(hex, out var colour) ? new SolidColorBrush(colour) : null;

    public static double StripeOpacity(bool hasListColor) => hasListColor ? 1.0 : 0.0;

    /// <summary>A completed row is dimmed until it moves on; on the Completed Smart View it keeps full strength.</summary>
    public static double RowOpacity(bool isCompleted, bool keepsFullStrength) =>
        isCompleted && !keepsFullStrength ? 0.6 : 1.0;

    public static TextDecorations TitleDecorations(bool isCompleted, bool keepsFullStrength) =>
        isCompleted && !keepsFullStrength ? TextDecorations.Strikethrough : TextDecorations.None;

    /// <summary>A Tag as a chip reads it: "#" and its name.</summary>
    public static string Hash(string name) => "#" + name;

    public Style DueChipStyle(DueState state) => (Style)Resources[state switch
    {
        DueState.Overdue => "DueChipOverdue",
        DueState.Today => "DueChipToday",
        _ => "DueChipLater",
    }];

    public Style DueTextStyle(DueState state) => (Style)Resources[state switch
    {
        DueState.Overdue => "DueTextOverdue",
        DueState.Today => "DueTextToday",
        _ => "DueTextLater",
    }];

    public Style PriorityFlagStyle(Priority priority) => (Style)Resources[priority switch
    {
        Priority.High => "PriorityFlagHigh",
        Priority.Medium => "PriorityFlagMedium",
        _ => "RowIcon",
    }];
}
