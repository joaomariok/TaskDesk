using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

/// <summary>The Repeat presets offered in the detail pane (SPEC 4.2).</summary>
public enum RepeatPreset
{
    None,
    Daily,
    Weekdays,
    Weekly,
    Monthly,
    Yearly,
}

/// <summary>What the owner chose when deleting an Occurrence (SPEC 5.2).</summary>
public enum OccurrenceDeleteChoice
{
    Cancel,

    /// <summary>A Skip: the next Occurrence is still produced.</summary>
    DeleteThisOccurrence,

    /// <summary>Delete this Occurrence and produce no next one.</summary>
    StopRepeating,
}

/// <summary>Asks the owner what deleting an Occurrence means; implemented by the UI.</summary>
public interface IOccurrenceDialogs
{
    OccurrenceDeleteChoice AskDeleteOccurrence(string taskTitle);
}

/// <summary>
/// The Repeat section of the detail pane: maps the presets onto a <see cref="Recurrence"/>. Every edit
/// calls back so the owner of the editor can save it.
/// </summary>
public sealed partial class RepeatEditorViewModel(Action changed) : ObservableObject
{
    private static readonly HashSet<string> EditableProperties =
    [
        nameof(Preset), nameof(Interval), nameof(Monday), nameof(Tuesday), nameof(Wednesday), nameof(Thursday),
        nameof(Friday), nameof(Saturday), nameof(Sunday), nameof(DayOfMonth), nameof(RepeatFromCompletion),
        nameof(EndDate),
    ];

    private bool _loading;

    // Yearly stores its month and day; a new Yearly Recurrence takes them from the Due date.
    private int _yearlyMonth = 1;
    private int _yearlyDay = 1;

    public static IReadOnlyList<RepeatPreset> Presets { get; } = Enum.GetValues<RepeatPreset>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRepeating))]
    [NotifyPropertyChangedFor(nameof(HasInterval))]
    [NotifyPropertyChangedFor(nameof(HasDays))]
    [NotifyPropertyChangedFor(nameof(HasDayOfMonth))]
    private RepeatPreset _preset;

    /// <summary>"Every N" days, weeks, months or years.</summary>
    [ObservableProperty]
    private int _interval = 1;

    [ObservableProperty]
    private bool _monday;

    [ObservableProperty]
    private bool _tuesday;

    [ObservableProperty]
    private bool _wednesday;

    [ObservableProperty]
    private bool _thursday;

    [ObservableProperty]
    private bool _friday;

    [ObservableProperty]
    private bool _saturday;

    [ObservableProperty]
    private bool _sunday;

    /// <summary>Monthly: the day N of the month (clamped to shorter months).</summary>
    [ObservableProperty]
    private int _dayOfMonth = 1;

    /// <summary>The Anchor: true counts from the completion date, false from the Due date.</summary>
    [ObservableProperty]
    private bool _repeatFromCompletion;

    /// <summary>Optional end date (a calendar date); no Occurrence after it.</summary>
    [ObservableProperty]
    private DateTime? _endDate;

    public bool IsRepeating => Preset != RepeatPreset.None;

    public bool HasInterval => Preset is not (RepeatPreset.None or RepeatPreset.Weekdays);

    public bool HasDays => Preset == RepeatPreset.Weekly;

    public bool HasDayOfMonth => Preset == RepeatPreset.Monthly;

    /// <summary>Shows a Task's Recurrence; the defaults for presets not chosen yet come from its Due date.</summary>
    internal void Load(Recurrence? recurrence, DateOnly? due)
    {
        _loading = true;
        try
        {
            Preset = recurrence switch
            {
                null => RepeatPreset.None,
                { Frequency: RecurrenceFrequency.Daily } => RepeatPreset.Daily,
                { Frequency: RecurrenceFrequency.Weekly } r when r.Interval == 1
                    && r.DaysOfWeek == Recurrence.Weekdays().DaysOfWeek => RepeatPreset.Weekdays,
                { Frequency: RecurrenceFrequency.Weekly } => RepeatPreset.Weekly,
                { Frequency: RecurrenceFrequency.Monthly } => RepeatPreset.Monthly,
                _ => RepeatPreset.Yearly,
            };
            Interval = recurrence?.Interval ?? 1;
            var days = recurrence is { Frequency: RecurrenceFrequency.Weekly }
                ? recurrence.DaysOfWeek
                : due?.DayOfWeek.ToRecurrenceDay() ?? RecurrenceDays.None;
            Monday = days.HasFlag(RecurrenceDays.Monday);
            Tuesday = days.HasFlag(RecurrenceDays.Tuesday);
            Wednesday = days.HasFlag(RecurrenceDays.Wednesday);
            Thursday = days.HasFlag(RecurrenceDays.Thursday);
            Friday = days.HasFlag(RecurrenceDays.Friday);
            Saturday = days.HasFlag(RecurrenceDays.Saturday);
            Sunday = days.HasFlag(RecurrenceDays.Sunday);
            DayOfMonth = recurrence?.DayOfMonth ?? due?.Day ?? 1;
            _yearlyMonth = recurrence?.Month ?? due?.Month ?? 1;
            _yearlyDay = recurrence?.DayOfMonth ?? due?.Day ?? 1;
            RepeatFromCompletion = recurrence?.Anchor == RecurrenceAnchor.CompletionDate;
            EndDate = recurrence?.EndDate?.ToDateTime(TimeOnly.MinValue);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>The Recurrence the editor shows, or null for no repeat.</summary>
    /// <exception cref="ArgumentException">The choices do not make a valid Recurrence.</exception>
    public Recurrence? Build()
    {
        var recurrence = Preset switch
        {
            RepeatPreset.None => null,
            RepeatPreset.Daily => Recurrence.Daily(Interval),
            RepeatPreset.Weekdays => Recurrence.Weekdays(),
            RepeatPreset.Weekly => Recurrence.Weekly(SelectedDays(), Interval),
            RepeatPreset.Monthly => Recurrence.Monthly(DayOfMonth, Interval),
            _ => Recurrence.Yearly(_yearlyMonth, _yearlyDay, Interval),
        };
        if (recurrence is null)
        {
            return null;
        }

        recurrence = recurrence with
        {
            Anchor = RepeatFromCompletion ? RecurrenceAnchor.CompletionDate : RecurrenceAnchor.DueDate,
            EndDate = EndDate is { } end ? DateOnly.FromDateTime(end) : null,
        };
        recurrence.Validate();
        return recurrence;
    }

    private RecurrenceDays SelectedDays() =>
        (Monday ? RecurrenceDays.Monday : 0) | (Tuesday ? RecurrenceDays.Tuesday : 0)
        | (Wednesday ? RecurrenceDays.Wednesday : 0) | (Thursday ? RecurrenceDays.Thursday : 0)
        | (Friday ? RecurrenceDays.Friday : 0) | (Saturday ? RecurrenceDays.Saturday : 0)
        | (Sunday ? RecurrenceDays.Sunday : 0);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is { } name && EditableProperties.Contains(name))
        {
            changed();
        }
    }
}
