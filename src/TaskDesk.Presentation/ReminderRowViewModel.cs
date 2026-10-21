using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Reminders;

namespace TaskDesk.Presentation;

/// <summary>One Reminder of the selected Task, as a row of the detail pane's Reminders section.</summary>
public sealed class ReminderRowViewModel
{
    private readonly string _wording;

    internal ReminderRowViewModel(
        Guid id, Guid taskId, ReminderValue value, DateTime firesAtUtc, string text, string wording, bool isPast,
        Action<ReminderRowViewModel> edit, Func<ReminderRowViewModel, Task> remove)
    {
        Id = id;
        TaskId = taskId;
        Value = value;
        FiresAtUtc = firesAtUtc;
        Text = text;
        _wording = wording;
        IsPast = isPast;
        EditCommand = new RelayCommand(() => edit(this));
        RemoveCommand = new AsyncRelayCommand(() => remove(this));
    }

    public Guid Id { get; }

    /// <summary>The Task the Reminder belongs to.</summary>
    public Guid TaskId { get; }

    /// <summary>What the Reminder is set to: a fixed instant, or an offset before the Due.</summary>
    public ReminderValue Value { get; }

    /// <summary>The instant the Reminder fires at.</summary>
    public DateTime FiresAtUtc { get; }

    /// <summary>
    /// The row's one line: when it fires, in local time ("Tomorrow 09:00", "Sun 25 Oct 09:00"); a before-Due
    /// Reminder leads with its compact offset ("1 day before · Sun 25 Oct 09:00").
    /// </summary>
    public string Text { get; }

    /// <summary>True when its moment has passed; the row stays listed, shown as past.</summary>
    public bool IsPast { get; }

    /// <summary>
    /// The full wording, for the row's tooltip: "1 day before Due, Sun 25 Oct 09:00", with ", past" for a past
    /// Reminder.
    /// </summary>
    public string Description => IsPast ? $"{_wording}, past" : _wording;

    /// <summary>What a screen reader announces for the row: its full wording and whether it is past.</summary>
    public string AccessibleName => $"Reminder {Description}";

    /// <summary>The accessible name of the row's remove button, naming the Reminder it removes.</summary>
    public string RemoveName => $"Remove reminder {_wording}";

    /// <summary>Opens the Reminder flyout on this Reminder.</summary>
    public IRelayCommand EditCommand { get; }

    /// <summary>Removes this Reminder.</summary>
    public IAsyncRelayCommand RemoveCommand { get; }
}
