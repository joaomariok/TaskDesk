namespace TaskDesk.Presentation;

// Where the detail pane shows ErrorMessage: under the field it is about, or under the pane's header.
public sealed partial class MainViewModel
{
    private enum ErrorPlace
    {
        General,
        Due,
        Reminder,
        Repeat,
    }

    private ErrorPlace _errorPlace;

    /// <summary>The error about the Due fields, or null.</summary>
    public string? DueError => ErrorAt(ErrorPlace.Due);

    /// <summary>The error about the Reminder fields, or null.</summary>
    public string? ReminderError => ErrorAt(ErrorPlace.Reminder);

    /// <summary>The error about the Repeat fields, or null.</summary>
    public string? RepeatError => ErrorAt(ErrorPlace.Repeat);

    /// <summary>An error that belongs to no field, such as a failed save, or null.</summary>
    public string? GeneralError => ErrorAt(ErrorPlace.General);

    private string? ErrorAt(ErrorPlace place) => _errorPlace == place ? ErrorMessage : null;

    /// <summary>Shows a validation error under its field. Any later message replaces it as a general one.</summary>
    private void ShowFieldError(ErrorPlace place, string message)
    {
        ErrorMessage = message;
        _errorPlace = place;
        NotifyErrorPlaces();
    }

    partial void OnErrorMessageChanged(string? value)
    {
        _errorPlace = ErrorPlace.General;
        NotifyErrorPlaces();
    }

    private void NotifyErrorPlaces()
    {
        OnPropertyChanged(nameof(DueError));
        OnPropertyChanged(nameof(ReminderError));
        OnPropertyChanged(nameof(RepeatError));
        OnPropertyChanged(nameof(GeneralError));
    }
}
