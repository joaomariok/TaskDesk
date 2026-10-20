using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TaskDesk.Presentation;

/// <summary>
/// The detail pane's Reminder flyout: a date and an optional time for a new Reminder, or for the Reminder being
/// edited. A date on its own fires at the default Reminder time. Save hands the values to the pane, which saves
/// them and closes the flyout; Cancel closes it without saving.
/// </summary>
public sealed partial class ReminderEditorViewModel : ObservableObject
{
    private readonly Func<TimeOnly> _defaultTime;
    private readonly Func<ReminderEditorViewModel, bool> _save;

    /// <param name="defaultTime">Reads the default Reminder time.</param>
    /// <param name="save">Saves the editor's values; returns false, keeping the flyout open, when they are invalid.</param>
    internal ReminderEditorViewModel(Func<TimeOnly> defaultTime, Func<ReminderEditorViewModel, bool> save)
    {
        _defaultTime = defaultTime;
        _save = save;
    }

    /// <summary>True while the flyout is open.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>The Reminder being edited, or null when the flyout adds a new one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(Heading))]
    private Guid? _editingId;

    /// <summary>True when the flyout edits an existing Reminder.</summary>
    public bool IsEditing => EditingId is not null;

    /// <summary>The flyout's heading.</summary>
    public string Heading => IsEditing ? "Edit reminder" : "Add reminder";

    /// <summary>The local date the Reminder fires on; clearing it clears the time.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDate))]
    private DateTime? _date;

    /// <summary>The time only applies once a date is set, so the flyout enables it then.</summary>
    public bool HasDate => Date is not null;

    /// <summary>The local time the Reminder fires at; null uses the default Reminder time.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearTimeCommand))]
    private TimeSpan? _time;

    /// <summary>Opens the flyout on a new Reminder, or on <paramref name="editingId"/> with its local date and time.</summary>
    internal void Open(Guid? editingId, DateTime? date, TimeSpan? time)
    {
        EditingId = editingId;
        Date = date;
        Time = time;
        IsOpen = true;
    }

    partial void OnDateChanged(DateTime? value)
    {
        if (value is null)
        {
            Time = null;
        }
    }

    /// <summary>
    /// Clears the time, so the Reminder fires at the default Reminder time. Disabled while the time already is the
    /// default, where clearing would change nothing.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClearTime))]
    private void ClearTime() => Time = null;

    private bool CanClearTime() => Time is { } shown && shown != _defaultTime().ToTimeSpan();

    [RelayCommand]
    private void Save()
    {
        if (IsOpen && _save(this))
        {
            IsOpen = false;
        }
    }

    [RelayCommand]
    private void Cancel() => IsOpen = false;
}
