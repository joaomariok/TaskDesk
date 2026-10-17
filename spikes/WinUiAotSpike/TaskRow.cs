using TaskDesk.Core.Tasks;

namespace WinUiAotSpike;

/// <summary>
/// One list row. Declared in the app assembly, sealed and partial, so the CsWinRT AOT source generator can
/// emit the COM vtables for it and for <c>ObservableCollection&lt;TaskRow&gt;</c>: binding a collection of
/// <see cref="TaskItem"/> (a type from TaskDesk.Core) directly failed under Native AOT with
/// "Value does not fall within the expected range" from <c>ItemsControl.ItemsSource</c>.
/// </summary>
public sealed partial class TaskRow(TaskItem task)
{
    public TaskItem Task { get; } = task;

    public string Title => Task.Title;
}
