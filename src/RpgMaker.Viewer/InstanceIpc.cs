using System.IO;
using System.IO.Pipes;
using System.Text;

namespace RpgMaker.Viewer;

/// <summary>
/// Forwards open-file requests from a second process instance to the running tray app,
/// so double-clicking another image works like a normal image viewer.
/// </summary>
internal sealed class InstanceIpc : IDisposable
{
    public const string PipeName = "RpgMakerImageViewer.OpenPipe";

    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _disposed;

    public event Action<string>? OpenPathReceived;

    public void StartServer()
    {
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public static bool TrySendOpenPath(string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(800);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line))
                    OpenPathReceived?.Invoke(line.Trim().Trim('"'));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                try { await Task.Delay(200, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _cts?.Dispose();
    }
}
