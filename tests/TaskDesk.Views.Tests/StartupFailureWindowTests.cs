using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Views.Dialogs;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The start-up failure path (#56): before the main window exists, a window of its own shows the explaining
/// dialog and closes when the owner presses OK. The exit code is covered by the FlaUI StartupFailureTests.
/// </summary>
[TestClass]
public sealed class StartupFailureWindowTests
{
    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task The_failure_window_explains_the_failure_in_a_dialog_and_closes_on_OK()
    {
        var ct = TestContext.CancellationToken;
        var window = new StartupFailureWindow();
        var closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            var shown = window.ShowAsync("TaskDesk could not open its database.\n\nNothing was deleted.");
            await UiWait.UntilAsync(() => window.Content?.XamlRoot is not null, () => "The window never loaded.", ct);
            var dialog = await DialogHost.OpenDialogAsync(window.Content!.XamlRoot, ct);

            Assert.AreEqual("TaskDesk", window.Title);
            Assert.AreEqual("TaskDesk", dialog.Title);
            CollectionAssert.Contains(DialogHost.Texts(dialog).ToList(),
                "TaskDesk could not open its database.\n\nNothing was deleted.");
            CollectionAssert.AreEqual(new[] { "OK" }, DialogHost.Buttons(dialog).Select(b => b.Content).ToList());
            Assert.AreEqual(ContentDialogButton.Close, dialog.DefaultButton);

            DialogHost.Press(dialog, "CloseButton");
            await shown;

            Assert.IsTrue(closed, "The failure window stayed open after OK.");
        }
        finally
        {
            if (!closed)
            {
                window.Close();
            }
        }
    }
}
