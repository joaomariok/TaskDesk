using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using TaskDesk.Core.Tasks;
using Windows.System;

namespace WinUiAotSpike;

/// <summary>The open Tasks of the Default List in a virtualizing ListView, plus a quick-add box.</summary>
public sealed partial class MainWindow : Window
{
    private readonly ITaskSource _tasks;
    private readonly ILogger _logger;
    private readonly ObservableCollection<TaskRow> _open = [];
    private bool _exiting;

    public MainWindow(ITaskSource tasks, ILogger logger)
    {
        _tasks = tasks;
        _logger = logger;
        InitializeComponent();
        TaskList.ItemsSource = _open;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 700));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "TaskDesk.ico"));

        // Closing hides to the tray, like the WPF app; only the tray's Exit really closes.
        AppWindow.Closing += (_, args) =>
        {
            if (!_exiting)
            {
                args.Cancel = true;
                AppWindow.Hide();
            }
        };
    }

    public async Task LoadAsync()
    {
        var all = await _tasks.ListDefaultListTasksAsync();
        _open.Clear();
        foreach (var task in all.Where(t => !t.IsCompleted))
        {
            _open.Add(new TaskRow(task));
        }

        StatusText.Text = $"{_open.Count} open Tasks in the Default List";
        _logger.LogInformation("Loaded {Count} open Tasks", _open.Count);
    }

    public void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
    }

    public void CloseForExit()
    {
        _exiting = true;
        Close();
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => _ = AddAsync();

    private void OnQuickAddKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            _ = AddAsync();
        }
    }

    private async Task AddAsync()
    {
        var title = QuickAddBox.Text;
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        try
        {
            var created = await _tasks.CreateTaskAsync(title);
            var row = new TaskRow(created);
            _open.Add(row);
            QuickAddBox.Text = "";
            TaskList.ScrollIntoView(row);
            StatusText.Text = $"{_open.Count} open Tasks in the Default List";
        }
        catch (ArgumentException ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}
