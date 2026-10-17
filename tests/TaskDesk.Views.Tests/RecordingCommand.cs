using System.Windows.Input;

namespace TaskDesk.Views.Tests;

/// <summary>A command that records each parameter it runs with.</summary>
public sealed partial class RecordingCommand : ICommand
{
    public List<object?> Executions { get; } = [];

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => Executions.Add(parameter);
}
