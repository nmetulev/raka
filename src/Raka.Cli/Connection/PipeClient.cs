using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Raka.Protocol;

namespace Raka.Cli.Connection;

/// <summary>
/// Named pipe client that connects to the Raka.DevTools pipe server inside a WinUI 3 app.
/// Uses raw byte I/O to avoid StreamReader/StreamWriter buffering issues with pipes.
/// </summary>
internal sealed class PipeClient : IDisposable
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;

    public PipeClient(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async Task ConnectAsync(int timeoutMs = 5000)
    {
        _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(timeoutMs);
    }

    public async Task<RakaResponse> SendAsync(RakaRequest request)
    {
        if (_pipe == null || !_pipe.IsConnected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

        // Write request as a single line
        var json = JsonSerializer.Serialize(request, RakaJson.Options);
        var requestBytes = Utf8NoBom.GetBytes(json + "\n");
        await _pipe.WriteAsync(requestBytes);
        await _pipe.FlushAsync();

        // Read response line (read bytes until we get a \n)
        var responseBuffer = new byte[256 * 1024];
        var totalRead = 0;

        while (true)
        {
            var bytesRead = await _pipe.ReadAsync(responseBuffer.AsMemory(totalRead));
            if (bytesRead == 0)
                throw new IOException("Pipe connection closed");

            totalRead += bytesRead;

            // Check if we have a complete line
            var text = Utf8NoBom.GetString(responseBuffer, 0, totalRead);
            var newlineIndex = text.IndexOf('\n');
            if (newlineIndex >= 0)
            {
                var responseLine = text[..newlineIndex].TrimEnd('\r');
                return JsonSerializer.Deserialize<RakaResponse>(responseLine, RakaJson.Options)
                    ?? throw new IOException("Invalid response from DevTools");
            }
        }
    }

    public async Task<RakaResponse> SendCommandAsync(string command, object? parameters = null)
    {
        var request = new RakaRequest
        {
            Command = command,
            Params = parameters != null
                ? JsonSerializer.SerializeToElement(parameters, RakaJson.Options)
                : null
        };

        return await SendAsync(request);
    }

    public void Dispose()
    {
        _pipe?.Dispose();
    }
}