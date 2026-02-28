using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Raka.Protocol;

namespace Raka.DevTools.Server;

/// <summary>
/// Named pipe server that listens for CLI commands inside the WinUI 3 app.
/// Runs on a background thread; dispatches to UI thread for visual tree operations.
/// </summary>
internal sealed class PipeServer : IDisposable
{
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
                // Handle each connection in its own task
                _ = Task.Run(() => HandleConnection(pipe, ct), ct);
            }
            catch (OperationCanceledException)
            {
                pipe.Dispose();
                break;
            }
            catch (Exception)
            {
                pipe.Dispose();
            }
        }
    }

    private async Task HandleConnection(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            using (pipe)
            using (var reader = new StreamReader(pipe, Encoding.UTF8))
            using (var writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true })
            {
                while (pipe.IsConnected && !ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break;

                    RakaRequest? request;
                    try
                    {
                        request = JsonSerializer.Deserialize<RakaRequest>(line, RakaJson.Options);
                    }
                    catch (JsonException)
                    {
                        var errorResponse = new RakaResponse
                        {
                            Success = false,
                            Error = "Invalid JSON request"
                        };
                        await writer.WriteLineAsync(JsonSerializer.Serialize(errorResponse, RakaJson.Options));
                        continue;
                    }

                    if (request == null) continue;

                    // Execute the command on the UI thread
                    var response = await DispatchToUIThread(request);
                    response.Id = request.Id;

                    await writer.WriteLineAsync(JsonSerializer.Serialize(response, RakaJson.Options));
                }
            }
        }
        catch (Exception)
        {
            // Connection broken — nothing to do
        }
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
