using System.Windows;
using TaskDesk.Core.Shell;
using TaskDesk.Presentation;

namespace TaskDesk.App;

/// <summary>Enum lists for the Settings window's drop-downs.</summary>
public static class SettingsOptions
{
    public static IReadOnlyList<ThemeChoice> Themes => SettingsViewModel.Themes;
    public static IReadOnlyList<CloseBehavior> CloseBehaviors => SettingsViewModel.CloseBehaviors;
}

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
