using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using TaskDesk.Core.Tasks;

namespace TaskDesk.App.Views;

/// <summary>The detail pane of a Task, opened on demand.</summary>
public partial class DetailPane : UserControl
{
    public DetailPane()
    {
        InitializeComponent();
    }

    /// <summary>Fills the Priority choices; called once by <see cref="MainWindow"/>.</summary>
    public void Initialize() => PriorityBox.ItemsSource = Enum.GetValues<Priority>();

    /// <summary>Focuses the title box with its text selected (F2, or Enter on a row).</summary>
    public void FocusTitle()
    {
        DetailTitleBox.Focus();
        DetailTitleBox.SelectAll();
    }
}

/// <summary>Turns a row's Subtask progress "2/5" into the pane's "2 of 5".</summary>
public sealed class SubtaskProgressTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string)?.Replace("/", " of ", StringComparison.Ordinal) ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
