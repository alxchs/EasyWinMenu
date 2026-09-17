using System.IO;
using System.IO.Pipes;

namespace QuickStacks.UI;

/// <summary>
/// Mantem exatamente uma instancia do QuickStacks viva por sessao de usuario (Fase 14, porta
/// direta do SingleInstanceCoordinator.cs do EasyWinMenu). Um segundo lancamento - por exemplo
/// um verbo de menu de contexto da area de trabalho (Fase 15) reabrindo o exe - repassa sua
/// linha de comando pra primeira instancia via named pipe e sai imediatamente, em vez de abrir
/// um segundo icone na bandeja e um segundo conjunto de grupos soltos.
/// </summary>
internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = "QuickStacks.SingleInstance";
    private const string PipeName = "QuickStacks.DesktopAction";

    private readonly Mutex _mutex;
    private CancellationTokenSource? _listenerCancellation;

    public bool IsFirstInstance { get; }

    public SingleInstanceCoordinator()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
    }

    /// <summary>Comeca a escutar comandos repassados por lancamentos seguintes. No-op numa segunda instancia.</summary>
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
                    // Um cliente desconectou no meio da escrita; a proxima iteracao abre um pipe novo.
                }
                catch (OperationCanceledException)
                {
                }
            }
        }, token);
    }

    /// <summary>Envia um comando pra instancia ja' rodando. Melhor esforco, no maximo meio segundo.</summary>
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
