using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskDesk.Presentation;
using Windows.UI.Text;

namespace TaskDesk.Views;

/// <summary>One Subtask row: what the Task list shows under an open Task for each of its Subtasks.</summary>
public sealed partial class SubtaskRow : UserControl
{
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
        nameof(Row), typeof(SubtaskListRowViewModel), typeof(SubtaskRow), new PropertyMetadata(null));

    public static readonly DependencyProperty ToggleCommandProperty = DependencyProperty.Register(
        nameof(ToggleCommand), typeof(ICommand), typeof(SubtaskRow), new PropertyMetadata(null));

    public SubtaskRow()
    {
        InitializeComponent();
    }

    /// <summary>The Subtask the row shows.</summary>
    public SubtaskListRowViewModel? Row
    {
        get => (SubtaskListRowViewModel?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    /// <summary>What the checkbox runs, with the row as its parameter: ticking or unticking the Subtask.</summary>
    public ICommand? ToggleCommand
    {
        get => (ICommand?)GetValue(ToggleCommandProperty);
        set => SetValue(ToggleCommandProperty, value);
    }

    /// <summary>A ticked Subtask, or the block of a completed Task, shows at 60 %.</summary>
    public static double RowOpacity(bool isDimmed) => isDimmed ? 0.6 : 1.0;

    public static TextDecorations TitleDecorations(bool isStruckThrough) =>
        isStruckThrough ? TextDecorations.Strikethrough : TextDecorations.None;
}
