using System.Windows;
using System.Windows.Controls;
using TaskDesk.Presentation;

namespace TaskDesk.App;

/// <summary>WPF implementation of the List dialogs: a message box and a small name prompt.</summary>
internal sealed class WpfListDialogs : IListDialogs
{
    public bool ConfirmDelete(string listName, int taskCount)
    {
        var message = $"Delete the list \"{listName}\" and all its tasks ({taskCount} open)? This cannot be undone.";
        return MessageBox.Show(
            message, "Delete list", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel)
            == MessageBoxResult.OK;
    }

    public string? PromptName(string currentName)
    {
        var box = new TextBox { Text = currentName, Margin = new Thickness(0, 0, 0, 12), MaxLength = 100 };
        var ok = new Button { Content = "OK", IsDefault = true, Width = 70, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 70 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(box);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = "Rename list",
            Content = panel,
            Width = 320,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
        };
        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };

        return window.ShowDialog() == true ? box.Text : null;
    }
}
