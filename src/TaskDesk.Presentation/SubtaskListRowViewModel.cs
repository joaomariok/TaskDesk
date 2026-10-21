using CommunityToolkit.Mvvm.ComponentModel;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

/// <summary>
/// A Subtask's row in the one Task list, under its open Task: the Subtask's title and tick, read from the last saved
/// state. Distinct from the detail pane's editable <see cref="SubtaskRowViewModel"/>.
/// </summary>
public sealed partial class SubtaskListRowViewModel : ObservableObject, ITaskListRow
{
    internal SubtaskListRowViewModel(Subtask subtask, TaskRowViewModel taskRow)
    {
        Id = subtask.Id;
        TaskRow = taskRow;
        Apply(subtask);
    }

    /// <summary>The Subtask's id.</summary>
    public Guid Id { get; }

    /// <summary>The Task the Subtask belongs to.</summary>
    public TaskRowViewModel TaskRow { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private string _title = "";

    /// <summary>Whether the Subtask is ticked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDimmed), nameof(IsStruckThrough))]
    private bool _isCompleted;

    /// <summary>What a screen reader calls the row: "Subtask" and its title.</summary>
    public string AccessibleName => "Subtask " + Title;

    /// <summary>
    /// True when the row shows at 60 %: a ticked Subtask, or any Subtask under a completed Task (the block dims
    /// once). Never on the Completed Smart View, whose rows keep full strength.
    /// </summary>
    public bool IsDimmed => !TaskRow.KeepsFullStrength && (IsCompleted || TaskRow.IsCompleted);

    /// <summary>True when the title is struck through: a ticked Subtask, except on the Completed Smart View.</summary>
    public bool IsStruckThrough => !TaskRow.KeepsFullStrength && IsCompleted;

    internal void Apply(Subtask subtask)
    {
        Title = subtask.Title;
        IsCompleted = subtask.IsCompleted;
    }

    // The Task's completion dims or undims the whole block.
    internal void OnTaskCompletionChanged() => OnPropertyChanged(nameof(IsDimmed));
}
