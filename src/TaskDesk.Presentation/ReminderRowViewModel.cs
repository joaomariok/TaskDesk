using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Reminders;

namespace TaskDesk.Presentation;

/// <summary>One Reminder of the selected Task, as a row of the detail pane's Reminders section.</summary>
public sealed class ReminderRowViewModel
{
    internal ReminderRowViewModel(
        Guid id, ReminderValue value, DateTime firesAtUtc, string text, bool isPast,
        Action<ReminderRowViewModel> edit, Func<ReminderRowViewModel, Task> remove)
    {
        Id = id;
        Value = value;
        FiresAtUtc = firesAtUtc;
        Text = text;
        IsPast = isPast;
        EditCommand = new RelayCommand(() => edit(this));
        RemoveCommand = new AsyncRelayCommand(() => remove(this));
    }

    public Guid Id { get; }

    /// <summary>What the Reminder is set to: a fixed instant, or an offset before the Due.</summary>
    public ReminderValue Value { get; }

    /// <summary>The instant the Reminder fires at.</summary>
    public DateTime FiresAtUtc { get; }

    /// <summary>
    /// When it fires, in local time ("Tomorrow 09:00"); a before-Due Reminder leads with its offset ("1 day before
    /// Due · Tomorrow 09:00").
    /// </summary>
    public string Text { get; }

    /// <summary>True when its moment has passed; the row stays listed, shown as past.</summary>
    public bool IsPast { get; }

    /// <summary>What a screen reader announces for the row: its text and whether it is past.</summary>
    public string AccessibleName => IsPast ? $"Reminder {Text}, past" : $"Reminder {Text}";

    /// <summary>The accessible name of the row's remove button, naming the Reminder it removes.</summary>
    public string RemoveName => $"Remove reminder {Text}";

    /// <summary>Opens the Reminder flyout on this Reminder.</summary>
    public IRelayCommand EditCommand { get; }

    /// <summary>Removes this Reminder.</summary>
    public IAsyncRelayCommand RemoveCommand { get; }
}
