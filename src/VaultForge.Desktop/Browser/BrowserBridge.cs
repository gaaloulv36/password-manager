using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using VaultForge.Core.Browser;

namespace VaultForge.Desktop.Browser;

/// <summary>
/// Named-pipe server the native host connects to. The pipe only accepts the current Windows user,
/// and each connection must come from our own VaultForge.NativeHost.exe.
/// </summary>
public sealed class BrowserBridge(BrowserRequestHandler handler, Dispatcher dispatcher, string trustedHostPath) : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public void Start() => _ = Task.Run(() => AcceptLoopAsync(_cts.Token));

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(BrowserProtocol.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => ServeAsync(pipe, cancellationToken), cancellationToken);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using var _ = pipe;
        if (!IsTrustedClient(pipe)) return;

        var connection = new BrowserConnection();
        using var writeLock = new SemaphoreSlim(1, 1);
        var inFlight = new List<Task>();
        try
        {
            while (await MessageFraming.ReadAsync(pipe, cancellationToken) is { } frame)
            {
                // Handled concurrently: waiting for a pairing decision mustn't block other requests.
                inFlight.Add(HandleFrameAsync(pipe, writeLock, connection, frame, cancellationToken));
                inFlight.RemoveAll(t => t.IsCompleted);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException)
        {
            // Browser closed the extension port.
        }
        await Task.WhenAll(inFlight);
    }

    private async Task HandleFrameAsync(Stream pipe, SemaphoreSlim writeLock, BrowserConnection connection, byte[] frame, CancellationToken cancellationToken)
    {
        JsonObject reply;
        try
        {
            var message = JsonNode.Parse(frame) as JsonObject ?? throw new JsonException();
            reply = await dispatcher.InvokeAsync(() => handler.HandleAsync(connection, message)).Task.Unwrap();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"Browser request failed: {ex}");
            reply = new JsonObject { ["type"] = "error", ["error"] = "internal" };
        }

        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await MessageFraming.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(reply), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Connection went away while we were answering.
        }
        finally
        {
            writeLock.Release();
        }
    }

    private bool IsTrustedClient(NamedPipeServerStream pipe)
    {
        try
        {
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var processId)) return false;
            using var client = Process.GetProcessById((int)processId);
            var path = client.MainModule?.FileName;
            return path is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(trustedHostPath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint clientProcessId);
}
