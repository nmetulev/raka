using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Raka.Protocol;

namespace Raka.DevTools.Server;

/// <summary>
/// Named pipe server that listens for CLI commands inside the WinUI 3 app.
/// Runs on a background thread; dispatches to UI thread for visual tree operations.
/// Uses raw byte I/O to avoid StreamReader/StreamWriter buffering issues with pipes.
/// </summary>
internal sealed class PipeServer : IDisposable
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private readonly string _pipeName;
    private readonly CommandRouter _router;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;

    public PipeServer(string pipeName, CommandRouter router, DispatcherQueue dispatcherQueue)
    {
        _pipeName = pipeName;
        _router = router;
        _dispatcherQueue = dispatcherQueue;
    }

    public void Start()
    {
        _listenTask = Task.Run(() => ListenLoop(_cts.Token));
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(ct);
                _ = HandleConnectionAsync(pipe, ct);
            }
            catch (OperationCanceledException)
            {
                pipe.Dispose();
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Raka DevTools] Listen error: {ex.Message}");
                pipe.Dispose();
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            using (pipe)
            {
                var buffer = new byte[64 * 1024];
                var leftover = string.Empty;

                while (pipe.IsConnected && !ct.IsCancellationRequested)
                {
                    int bytesRead;
                    try
                    {
                        bytesRead = await pipe.ReadAsync(buffer, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (IOException)
                    {
                        break;
                    }

                    if (bytesRead == 0) break;

                    var text = leftover + Utf8NoBom.GetString(buffer, 0, bytesRead);
                    var lines = text.Split('\n');

                    // Last element is either empty (if text ended with \n) or a partial line
                    leftover = lines[^1];

                    for (int i = 0; i < lines.Length - 1; i++)
                    {
                        var line = lines[i].TrimEnd('\r');
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var responseJson = await ProcessLine(line);
                        var responseBytes = Utf8NoBom.GetBytes(responseJson + "\n");

                        try
                        {
                            await pipe.WriteAsync(responseBytes, ct);
                            await pipe.FlushAsync(ct);
                        }
                        catch (IOException)
                        {
                            return;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Raka DevTools] Connection error: {ex.Message}");
        }
    }

    private async Task<string> ProcessLine(string line)
    {
        RakaRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<RakaRequest>(line, RakaJson.Options);
        }
        catch (JsonException ex)
        {
            var errorResponse = new RakaResponse { Success = false, Error = $"Invalid JSON: {ex.Message}" };
            return JsonSerializer.Serialize(errorResponse, RakaJson.Options);
        }

        if (request == null)
        {
            var errorResponse = new RakaResponse { Success = false, Error = "Null request" };
            return JsonSerializer.Serialize(errorResponse, RakaJson.Options);
        }

        var response = await DispatchToUIThread(request);
        response.Id = request.Id;
        return JsonSerializer.Serialize(response, RakaJson.Options);
    }

    private Task<RakaResponse> DispatchToUIThread(RakaRequest request)
    {
        var tcs = new TaskCompletionSource<RakaResponse>();

        bool queued = _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var response = _router.Handle(request);
                tcs.SetResult(response);
            }
            catch (Exception ex)
            {
                tcs.SetResult(new RakaResponse
                {
                    Id = request.Id,
                    Success = false,
                    Error = ex.Message
                });
            }
        });

        if (!queued)
        {
            tcs.SetResult(new RakaResponse
            {
                Id = request.Id,
                Success = false,
                Error = "Failed to dispatch to UI thread"
            });
        }

        return tcs.Task;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}