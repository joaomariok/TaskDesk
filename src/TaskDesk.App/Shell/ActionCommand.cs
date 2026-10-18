using System.Windows.Input;

namespace TaskDesk.App.Shell;

/// <summary>An always-enabled command. Partial so CsWinRT can generate its AOT-safe COM wrappers.</summary>
internal sealed partial class ActionCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
