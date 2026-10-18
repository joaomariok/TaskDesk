using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using Windows.System;

namespace TaskDesk.Views.Tests;

/// <summary>
/// Ctrl+comma opens Settings from anywhere in the main window: the window's content handles the key before a text
/// box does (WinUI invokes no keyboard accelerator for the comma key). The real key press is the UI tests'
/// (SettingsWindowTests in TaskDesk.UiTests).
/// </summary>
[TestClass]
public sealed class SettingsShortcutTests
{
    private const VirtualKey Comma = (VirtualKey)0xBC; // VK_OEM_COMMA

    [UITestMethod]
    public void Ctrl_comma_and_only_Ctrl_comma_asks_for_Settings()
    {
        using var db = new TempDatabase();
        var viewModel = new MainViewModel(db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock);
        var window = new MainWindow(viewModel);
        var requests = 0;
        viewModel.SettingsRequested += (_, _) => requests++;
        try
        {
            Assert.IsFalse(window.TryShortcut(Comma, VirtualKeyModifiers.None), "A plain comma asked for Settings.");
            Assert.IsFalse(window.TryShortcut(Comma, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift));
            Assert.IsFalse(window.TryShortcut(VirtualKey.S, VirtualKeyModifiers.Control));
            Assert.AreEqual(0, requests);

            Assert.IsTrue(window.TryShortcut(Comma, VirtualKeyModifiers.Control));

            Assert.AreEqual(1, requests, "Ctrl+comma did not ask for Settings.");
        }
        finally
        {
            window.Close();
        }
    }
}
