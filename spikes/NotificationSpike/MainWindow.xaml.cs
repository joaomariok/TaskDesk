using System.Windows;

namespace NotificationSpike;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StatusText.Text = App.StatusLine;
        foreach (var line in App.History)
        {
            LogBox.AppendText(line + Environment.NewLine);
        }

        App.LogWritten += OnLog;
        Closed += (_, _) => App.LogWritten -= OnLog;
    }

    private void OnLog(string line) =>
        Dispatcher.BeginInvoke(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        });

    private void ShowNow_Click(object sender, RoutedEventArgs e) => App.ShowToast();

    private async void ShowDelayed_Click(object sender, RoutedEventArgs e)
    {
        App.Log("Delayed toast requested; you may close this window now (it ends the process).");
        await Task.Delay(TimeSpan.FromSeconds(15));
        App.ShowToast();
    }
}
