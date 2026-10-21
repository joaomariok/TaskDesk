using CommunityToolkit.Mvvm.ComponentModel;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

/// <summary>A Subtask in the detail pane. Edits made in the view are reported through the callback.</summary>
public sealed partial class SubtaskRowViewModel : ObservableObject
{
    private readonly Action<SubtaskRowViewModel, SubtaskEdit> _edited;
    private bool _applying;

    internal SubtaskRowViewModel(Subtask subtask, Action<SubtaskRowViewModel, SubtaskEdit> edited)
    {
        Id = subtask.Id;
        _edited = edited;
        Apply(subtask);
    }

    public Guid Id { get; }

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private bool _isCompleted;

    partial void OnTitleChanged(string value)
    {
        if (!_applying)
        {
            _edited(this, SubtaskEdit.Title);
        }
    }

    partial void OnIsCompletedChanged(bool value)
    {
        if (!_applying)
        {
            _edited(this, SubtaskEdit.Completed);
        }
    }

    // The tick as saved from the Task list; the title in the field stays as typed.
    internal void ApplyCompleted(bool isCompleted)
    {
        _applying = true;
        IsCompleted = isCompleted;
        _applying = false;
    }

    internal void Apply(Subtask subtask)
    {
        _applying = true;
        Title = subtask.Title;
        IsCompleted = subtask.IsCompleted;
        _applying = false;
    }
}

public enum SubtaskEdit
{
    Title,
    Completed,
}
