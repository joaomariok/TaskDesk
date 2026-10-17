using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TaskDesk.Core.Import;
using TaskDesk.Presentation;

namespace TaskDesk.App;

/// <summary>WPF implementation of the import dialogs: Open file, a Merge/Replace choice and a Replace warning.</summary>
internal sealed class WpfImportDialogs : IImportDialogs
{
    public string? PickImportPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import a TaskDesk JSON export",
            DefaultExt = ".json",
            Filter = "JSON file (*.json)|*.json",
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public ImportMode? ChooseImportMode(string path)
    {
        ImportMode? choice = null;
        var message = new TextBlock
        {
            Text = $"How should \"{Path.GetFileName(path)}\" be imported?\n\n"
                + "Merge: add the lists, tags and tasks that are not here yet. Nothing here is changed or deleted.\n\n"
                + "Replace: delete ALL current lists, tags and tasks and use the file's data instead. "
                + "A backup is taken first.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var merge = new Button { Content = "Merge", IsDefault = true, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0) };
        var replace = new Button { Content = "Replace everything…", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(8, 2, 8, 2) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(merge);
        buttons.Children.Add(replace);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(message);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = "Import",
            Content = panel,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = ActiveWindow(),
        };
        merge.Click += (_, _) =>
        {
            choice = ImportMode.Merge;
            window.DialogResult = true;
        };
        replace.Click += (_, _) =>
        {
            choice = ImportMode.Replace;
            window.DialogResult = true;
        };
        window.Loaded += (_, _) => merge.Focus();

        return window.ShowDialog() == true ? choice : null;
    }

    public bool ConfirmReplace()
    {
        const string text =
            "Replace deletes all your current lists, tags and tasks and replaces them with the file's data.\n\n"
            + "A backup of the current data is saved in the backups folder first.\n\nReplace everything now?";
        const string caption = "Replace all data";
        var owner = ActiveWindow();
        var answer = owner is null
            ? MessageBox.Show(text, caption, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel)
            : MessageBox.Show(owner, text, caption, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        return answer == MessageBoxResult.OK;
    }

    private static Window? ActiveWindow() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
}
