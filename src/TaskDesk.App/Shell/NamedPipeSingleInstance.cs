using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Shell;

/// <summary>
/// Named mutex for ownership plus a named pipe that wakes the first instance and forwards a later launch's
/// arguments (wire format: <see cref="SecondLaunchMessage"/>). Names come from <see cref="SingleInstanceNames"/>,
/// so a redirected data folder is its own instance. Covered by manual checks and the UI tests.
/// </summary>
internal sealed partial class NamedPipeSingleInstance(SingleInstanceNames names) : ISingleInstance
{
    private readonly CancellationTokenSource _stop = new();
    private Mutex? _mutex;
    private bool? _owned;

    /// <summary>Idempotent: the first call decides, later calls return the same answer.</summary>
    public bool TryAcquire()
    {
        if (_owned is { } owned)
        {
            return owned;
        }

        _mutex = new Mutex(initiallyOwned: true, names.Mutex, out var createdNew);
        _owned = createdNew;
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
        }

        return createdNew;
    }

    public async Task SignalExistingAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        // This launch has the foreground; let the running instance take it when it shows its window.
        AllowSetForegroundWindow(AsfwAny);
        try
        {
            await using var client = new NamedPipeClientStream(".", names.Pipe, PipeDirection.Out);
            await client.ConnectAsync(5000, cancellationToken);
            await SecondLaunchMessage.WriteAsync(client, arguments, cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            // The first instance is not listening (yet); nothing more to do.
        }
    }

    public void Listen(Action<IReadOnlyList<string>> onSecondLaunch)
    {
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        names.Pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_stop.Token);
                    var arguments = await SecondLaunchMessage.ReadAsync(server, _stop.Token);
                    onSecondLaunch(arguments);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    // Client vanished mid-handshake; keep listening.
                }
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
            _mutex = null;
        }

        _stop.Dispose();
    }

    private const int AsfwAny = -1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
