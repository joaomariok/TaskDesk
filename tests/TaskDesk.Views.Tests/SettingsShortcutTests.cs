using Microsoft.UI.Xaml.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using Windows.System;

namespace TaskDesk.Views.Tests;

/// <summary>
/// Ctrl+comma opens Settings from anywhere in the main window: a window-wide keyboard accelerator on the main
/// window's content. The real key press is the UI tests' (SettingsWindowTests in TaskDesk.UiTests).
/// </summary>
[TestClass]
public sealed class SettingsShortcutTests
{
    private const VirtualKey Comma = (VirtualKey)0xBC; // VK_OEM_COMMA

    [UITestMethod]
    public void The_main_window_has_a_hidden_Ctrl_comma_accelerator()
    {
        using var db = new TempDatabase();
        var viewModel = new MainViewModel(db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock);
        var window = new MainWindow(viewModel);
        try
        {
            var accelerator = window.Navigation.KeyboardAccelerators
                .SingleOrDefault(a => a.Key == Comma && a.Modifiers == VirtualKeyModifiers.Control);

            Assert.IsNotNull(accelerator, "No Ctrl+comma accelerator on the main window's content.");
            Assert.IsTrue(accelerator.IsEnabled);
            Assert.AreEqual(KeyboardAcceleratorPlacementMode.Hidden, window.Navigation.KeyboardAcceleratorPlacementMode);
        }
        finally
        {
            window.Close();
        }
    }
}
