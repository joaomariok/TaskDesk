namespace TaskDesk.Core.Import;

/// <summary>The file cannot be imported. The message is meant for the owner. Nothing was changed.</summary>
public sealed class ImportException : Exception
{
    public ImportException(string message)
        : base(message)
    {
    }

    public ImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
