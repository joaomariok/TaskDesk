namespace TaskDesk.Core.Reminders;

/// <summary>
/// How often a Task Nags after its Due. The sub-day periods count elapsed time; <see cref="OneDay"/> keeps the
/// local time of day across a daylight-saving change. A Task without a Nag has none (null).
/// </summary>
public enum NagPeriod
{
    /// <summary>Every 15 minutes.</summary>
    FifteenMinutes = 0,

    /// <summary>Every 30 minutes.</summary>
    ThirtyMinutes = 1,

    /// <summary>Every hour.</summary>
    OneHour = 2,

    /// <summary>Every 3 hours.</summary>
    ThreeHours = 3,

    /// <summary>Every day, at the Due moment's local time of day.</summary>
    OneDay = 4,
}
