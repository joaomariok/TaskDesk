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
            ShowFieldError(ErrorPlace.Repeat, "A repeating Task needs a Due date.");
            return;
        }

        Recurrence? recurrence;
        try
        {
            recurrence = Repeat.Build();
        }
        catch (ArgumentException ex)
        {
            ShowFieldError(ErrorPlace.Repeat, ex.Message);
            return;
        }

        _ = Saves.Enqueue(() => StoreRecurrenceAsync(row, recurrence), accepted: () => ErrorMessage = null);
    }

    private async Task<bool> StoreRecurrenceAsync(TaskRowViewModel row, Recurrence? recurrence)
    {
        row.Apply(await Recurrences.SetRecurrenceAsync(row.Id, recurrence));
        return true;
    }
}
