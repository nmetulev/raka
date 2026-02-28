using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Raka.Protocol;

namespace Raka.Cli.Connection;

/// <summary>
/// Named pipe client that connects to the Raka.DevTools pipe server inside a WinUI 3 app.
/// </summary>
internal sealed class PipeClient : IDisposable
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public PipeClient(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async Task ConnectAsync(int timeoutMs = 5000)
    {
        _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(timeoutMs);
        _reader = new StreamReader(_pipe, Encoding.UTF8);
        _writer = new StreamWriter(_pipe, Encoding.UTF8) { AutoFlush = true };
    }

    public async Task<RakaResponse> SendAsync(RakaRequest request)
    {
        if (_writer == null || _reader == null)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

        var json = JsonSerializer.Serialize(request, RakaJson.Options);
        await _writer.WriteLineAsync(json);

        var responseLine = await _reader.ReadLineAsync()
            ?? throw new IOException("Pipe connection closed");

        return JsonSerializer.Deserialize<RakaResponse>(responseLine, RakaJson.Options)
            ?? throw new IOException("Invalid response from DevTools");
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
        _reader?.Dispose();
        _writer?.Dispose();
        _pipe?.Dispose();
    }
}
