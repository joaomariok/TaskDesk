using TaskDesk.App.Dialogs;
using TaskDesk.Presentation;

namespace TaskDesk.App;

/// <summary>WPF implementation of the List dialogs, both on the shared Fluent dialog.</summary>
internal sealed class WpfListDialogs : IListDialogs
{
    public bool ConfirmDelete(string listName, int taskCount)
    {
        var body = $"Delete the list \"{listName}\" and all its tasks ({taskCount} open)? This cannot be undone.";
        return FluentDialogService.Confirm("Delete list", body, "Delete");
    }

    public string? PromptName(string currentName)
    {
        var result = FluentDialogService.Show(new FluentDialogRequest("Rename list", "Choose a new name for this list.", ["Rename"])
        {
            Input = new FluentDialogInput("List name", currentName),
        });
        return result.IsCancelled ? null : result.Text;
    }
}
