using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Reminders;

namespace TaskDesk.Presentation;

/// <summary>
/// The detail pane's Reminder flyout, for a new Reminder or the Reminder being edited, in one of two modes. Date and
/// time takes a date and an optional time; a date on its own fires at the default Reminder time. Before Due takes a
/// preset offset, or "Custom…" with a whole number of minutes, hours, days or weeks, and is available only while the
/// Task has a Due. Save hands the values to the pane, which saves them and closes the flyout; Cancel closes it
/// without saving.
/// </summary>
public sealed partial class ReminderEditorViewModel : ObservableObject
{
    /// <summary>The <see cref="ModeIndex"/> of the Date and time mode.</summary>
    public const int DateAndTimeMode = 0;

    /// <summary>The <see cref="ModeIndex"/> of the Before Due mode.</summary>
    public const int BeforeDueMode = 1;

    private static readonly (int Amount, ReminderUnit Unit)[] Presets =
    [
        (5, ReminderUnit.Minutes), (15, ReminderUnit.Minutes), (30, ReminderUnit.Minutes),
        (1, ReminderUnit.Hours), (2, ReminderUnit.Hours),
        (1, ReminderUnit.Days), (2, ReminderUnit.Days),
        (1, ReminderUnit.Weeks),
    ];

    private static readonly ReminderUnit[] Units =
        [ReminderUnit.Minutes, ReminderUnit.Hours, ReminderUnit.Days, ReminderUnit.Weeks];

    /// <summary>The Before Due choices: the presets, then "Custom…" (<see cref="CustomIndex"/>).</summary>
    public static IReadOnlyList<string> OffsetChoices { get; } =
        ["5 min", "15 min", "30 min", "1 h", "2 h", "1 day", "2 days", "1 week", "Custom…"];

    /// <summary>The units "Custom…" offers, in the order of <see cref="CustomUnitIndex"/>.</summary>
    public static IReadOnlyList<string> UnitChoices { get; } = ["minutes", "hours", "days", "weeks"];

    /// <summary>The <see cref="OffsetIndex"/> of "Custom…".</summary>
    public static int CustomIndex => Presets.Length;

    private const int DefaultUnitIndex = 2; // days

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

    /// <summary>The mode: <see cref="DateAndTimeMode"/> or <see cref="BeforeDueMode"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBeforeDue), nameof(IsDateAndTime), nameof(IsCustomShown))]
    private int _modeIndex;

    /// <summary>True in the Before Due mode.</summary>
    public bool IsBeforeDue => ModeIndex == BeforeDueMode;

    /// <summary>True in the Date and time mode.</summary>
    public bool IsDateAndTime => !IsBeforeDue;

    /// <summary>Whether the Before Due mode is available: only while the Task has a Due.</summary>
    [ObservableProperty]
    private bool _canUseBeforeDue;

    /// <summary>The picked Before Due choice, an index into <see cref="OffsetChoices"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom), nameof(IsCustomShown))]
    private int _offsetIndex;

    /// <summary>True when "Custom…" is picked.</summary>
    public bool IsCustom => OffsetIndex == CustomIndex;

    /// <summary>True when the Custom number and unit show: in the Before Due mode with "Custom…" picked.</summary>
    public bool IsCustomShown => IsBeforeDue && IsCustom;

    /// <summary>The Custom amount; a whole number from 1 to 999 saves. NaN while the number box is empty.</summary>
    [ObservableProperty]
    private double _customAmount = ReminderValue.MinAmount;

    /// <summary>The Custom unit, an index into <see cref="UnitChoices"/>.</summary>
    [ObservableProperty]
    private int _customUnitIndex = DefaultUnitIndex;

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

    /// <summary>
    /// The Before Due value the editor holds: the picked preset, or the Custom amount and unit; null when the Custom
    /// amount is not a whole number from 1 to 999.
    /// </summary>
    public ReminderValue? BeforeDueValue()
    {
        if (!IsCustom)
        {
            var (amount, unit) = Presets[Math.Clamp(OffsetIndex, 0, Presets.Length - 1)];
            return ReminderValue.BeforeDue(amount, unit);
        }

        if (double.IsNaN(CustomAmount) || CustomAmount != Math.Floor(CustomAmount)
            || CustomAmount < ReminderValue.MinAmount || CustomAmount > ReminderValue.MaxAmount
            || CustomUnitIndex < 0 || CustomUnitIndex >= Units.Length)
        {
            return null;
        }

        return ReminderValue.BeforeDue((int)CustomAmount, Units[CustomUnitIndex]);
    }

    /// <summary>
    /// Opens the flyout on a new Reminder (Date and time, empty), or on <paramref name="editingId"/>: a Fixed
    /// Reminder with its local date and time, or a before-Due one with its offset.
    /// </summary>
    internal void Open(Guid? editingId, DateTime? date, TimeSpan? time, ReminderValue? beforeDue, bool canUseBeforeDue)
    {
        EditingId = editingId;
        Date = date;
        Time = time;
        CanUseBeforeDue = canUseBeforeDue;
        OffsetIndex = 0;
        CustomAmount = ReminderValue.MinAmount;
        CustomUnitIndex = DefaultUnitIndex;
        if (beforeDue is { Amount: { } amount, Unit: { } unit })
        {
            var preset = Array.IndexOf(Presets, (amount, unit));
            OffsetIndex = preset >= 0 ? preset : CustomIndex;
            if (preset < 0)
            {
                CustomAmount = amount;
                CustomUnitIndex = Array.IndexOf(Units, unit);
            }
        }

        ModeIndex = beforeDue is null ? DateAndTimeMode : BeforeDueMode;
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
