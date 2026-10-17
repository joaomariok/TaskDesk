using TaskDesk.App.Dialogs;
using TaskDesk.Presentation;

namespace TaskDesk.App;

/// <summary>Asks "Delete this occurrence" / "Stop repeating" when an Occurrence is deleted (SPEC 5.2).</summary>
internal sealed class WpfOccurrenceDialogs : IOccurrenceDialogs
{
    public OccurrenceDeleteChoice AskDeleteOccurrence(string taskTitle)
    {
        var body = $"\"{taskTitle}\" repeats. Delete only this occurrence (the next one is still created), "
            + "or stop repeating?";
        var result = FluentDialogService.Show(
            new FluentDialogRequest("Delete repeating task", body, ["Delete this occurrence", "Stop repeating"]));
        return result.ChoiceIndex switch
        {
            0 => OccurrenceDeleteChoice.DeleteThisOccurrence,
            1 => OccurrenceDeleteChoice.StopRepeating,
            _ => OccurrenceDeleteChoice.Cancel,
        };
    }
}
