// Native messaging host: started by the browser when the extension connects.
// It only relays framed messages between the extension (stdin/stdout) and the desktop app (named pipe).
// Everything after pairing is end-to-end encrypted between extension and app, so this process sees no secrets.
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using VaultForge.Core.Browser;

var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();

var pipe = await ConnectToAppAsync(TimeSpan.FromSeconds(1));
if (pipe is null)
{
    // The app isn't running. Start it if the extension asks; otherwise answer and exit, rather than
    // idling in the browser (an idle host would keep its files locked and block app updates).
    if (await MessageFraming.ReadAsync(stdin) is not { } frame) return 0;

    string? id = null;
    var wantsLaunch = false;
    try
    {
        var message = JsonDocument.Parse(frame).RootElement;
        id = message.TryGetProperty("id", out var i) ? i.GetString() : null;
        wantsLaunch = message.TryGetProperty("type", out var type) && type.GetString() == "launch-app";
    }
    catch (JsonException)
    {
    }

    if (wantsLaunch && TryLaunchApp() && await ConnectToAppAsync(TimeSpan.FromSeconds(15)) is { } started)
    {
        pipe = started;
        await Reply(stdout, new { type = "app-started", id });
    }
    else
    {
        await Reply(stdout, new { type = "app-not-running", id });
        return 0;
    }
}

await using (pipe)
{
    var toApp = Relay(stdin, pipe);
    var toBrowser = Relay(pipe, stdout);
    await Task.WhenAny(toApp, toBrowser); // either side closing ends the session
}
return 0;

static async Task<NamedPipeClientStream?> ConnectToAppAsync(TimeSpan timeout)
{
    var pipe = new NamedPipeClientStream(".", BrowserProtocol.PipeName, PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    try
    {
        await pipe.ConnectAsync((int)timeout.TotalMilliseconds);
        return pipe;
    }
    catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
    {
        await pipe.DisposeAsync();
        return null;
    }
}

static bool TryLaunchApp()
{
    var app = Path.Combine(AppContext.BaseDirectory, "VaultForge.exe");
    if (!File.Exists(app)) return false;
    Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
    return true;
}

static async Task Relay(Stream from, Stream to)
{
    try
    {
        while (await MessageFraming.ReadAsync(from) is { } frame)
            await MessageFraming.WriteAsync(to, frame);
    }
    catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException)
    {
        // Browser closed the port or the app exited.
    }
}

static Task Reply(Stream stdout, object message) => MessageFraming.WriteAsync(stdout, JsonSerializer.SerializeToUtf8Bytes(message));
