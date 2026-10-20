using CommunityToolkit.Mvvm.Input;

namespace TaskDesk.Presentation;

/// <summary>One Reminder of the selected Task, as a row of the detail pane's Reminders section.</summary>
public sealed class ReminderRowViewModel
{
    internal ReminderRowViewModel(
        Guid id, DateTime firesAtUtc, string text, bool isPast, Action<ReminderRowViewModel> edit,
        Func<ReminderRowViewModel, Task> remove)
    {
        Id = id;
        FiresAtUtc = firesAtUtc;
        Text = text;
        IsPast = isPast;
        EditCommand = new RelayCommand(() => edit(this));
        RemoveCommand = new AsyncRelayCommand(() => remove(this));
    }

    public Guid Id { get; }

    /// <summary>The instant the Reminder fires at.</summary>
    public DateTime FiresAtUtc { get; }

    /// <summary>When it fires, in local time ("Tomorrow 09:00").</summary>
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
