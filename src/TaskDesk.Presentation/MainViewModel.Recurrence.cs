using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

// The Repeat section of the detail pane.
public sealed partial class MainViewModel
{
    private RepeatEditorViewModel? _repeat;

    /// <summary>The Repeat section of the detail pane, showing the selected Task's Recurrence.</summary>
    public RepeatEditorViewModel Repeat => _repeat ??= new RepeatEditorViewModel(OnRepeatEdited);

    private void OnRepeatEdited()
    {
        if (DetailTask is not { } row)
        {
            return;
        }

        if (Repeat.IsRepeating && DetailDueDate is null)
        {
            ErrorMessage = "A repeating Task needs a Due date.";
            return;
        }

        Recurrence? recurrence;
        try
        {
            recurrence = Repeat.Build();
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        ErrorMessage = null;
        _pendingSave = SaveRecurrenceAsync(_pendingSave, row, recurrence);
    }

    private async Task SaveRecurrenceAsync(Task previous, TaskRowViewModel row, Recurrence? recurrence)
    {
        await previous; // saves of one Task never overlap
        try
        {
            var updated = await Recurrences.SetRecurrenceAsync(row.Id, recurrence);
            row.Apply(updated);
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = "Could not save the changes: " + ex.Message;
        }
    }
}
