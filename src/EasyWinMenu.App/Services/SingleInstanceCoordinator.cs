using System.IO;
using System.IO.Pipes;

namespace EasyWinMenu.App.Services;

/// <summary>
/// Keeps exactly one EasyWinMenu instance alive per user session. A second launch —
/// typically Windows invoking the exe again for a desktop context-menu verb — hands its
/// command line to the first instance over a named pipe and exits immediately, instead
/// of opening a second tray icon and a duplicate set of desktop groups.
/// </summary>
internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = "EasyWinMenu.SingleInstance";
    private const string PipeName = "EasyWinMenu.DesktopAction";

    private readonly Mutex _mutex;
    private CancellationTokenSource? _listenerCancellation;

    public bool IsFirstInstance { get; }

    public SingleInstanceCoordinator()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
    }

    /// <summary>Starts listening for commands forwarded by later launches. No-op on a second instance.</summary>
    public void StartListening(Action<string> onCommandReceived)
    {
        if (!IsFirstInstance)
        {
            return;
        }

        _listenerCancellation = new CancellationTokenSource();
        var token = _listenerCancellation.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server);
                    var command = await reader.ReadLineAsync(token);
                    if (!string.IsNullOrEmpty(command))
                    {
                        onCommandReceived(command);
                    }
                }
                catch (IOException)
                {
                    // A client disconnected mid-write; the next loop iteration opens a fresh pipe.
                }
                catch (OperationCanceledException)
                {
                }
            }
        }, token);
    }

    /// <summary>Sends a command to the already-running instance. Best-effort, half a second at most.</summary>
    public static bool TrySendToRunningInstance(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(500);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(command);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _listenerCancellation?.Cancel();

        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
