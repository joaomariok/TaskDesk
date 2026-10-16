using System.Windows;
using System.Windows.Controls;
using TaskDesk.Presentation;

namespace TaskDesk.App;

/// <summary>Asks "Delete this occurrence" / "Stop repeating" when an Occurrence is deleted (SPEC 5.2).</summary>
internal sealed class WpfOccurrenceDialogs : IOccurrenceDialogs
{
    public OccurrenceDeleteChoice AskDeleteOccurrence(string taskTitle)
    {
        var choice = OccurrenceDeleteChoice.Cancel;
        var message = new TextBlock
        {
            Text = $"\"{taskTitle}\" repeats. Delete only this occurrence (the next one is still created), "
                + "or stop repeating?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var skip = new Button { Content = "Delete this occurrence", IsDefault = true, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0) };
        var stop = new Button { Content = "Stop repeating", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(8, 2, 8, 2) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(skip);
        buttons.Children.Add(stop);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(message);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = "Delete repeating task",
            Content = panel,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
        };
        skip.Click += (_, _) =>
        {
            choice = OccurrenceDeleteChoice.DeleteThisOccurrence;
            window.DialogResult = true;
        };
        stop.Click += (_, _) =>
        {
            choice = OccurrenceDeleteChoice.StopRepeating;
            window.DialogResult = true;
        };
        window.Loaded += (_, _) => skip.Focus();

        return window.ShowDialog() == true ? choice : OccurrenceDeleteChoice.Cancel;
    }
}
