using TaskDesk.App.Dialogs;
using TaskDesk.Presentation;

namespace TaskDesk.App;

/// <summary>WPF implementation of the List dialogs, both on the shared Fluent dialog.</summary>
internal sealed class WpfListDialogs : IListDialogs
{
    // The WPF dialog is modal and synchronous; each result is returned as a completed task.
    public Task<bool> ConfirmDeleteAsync(string listName, int taskCount)
    {
        var body = $"Delete the list \"{listName}\" and all its tasks ({taskCount} open)? This cannot be undone.";
        return Task.FromResult(FluentDialogService.Confirm("Delete list", body, "Delete", destructive: true));
    }

    public Task<string?> PromptNameAsync(string currentName)
    {
        var result = FluentDialogService.Show(new FluentDialogRequest("Rename list", "Choose a new name for this list.", ["Rename"])
        {
            Input = new FluentDialogInput("List name", currentName),
        });
        return Task.FromResult(result.IsCancelled ? null : result.Text);
    }
}
