using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Raka.DevTools.Server;
using Raka.Protocol;

namespace Raka.DevTools;

/// <summary>
/// Extension methods to enable Raka DevTools in a WinUI 3 application.
/// </summary>
public static class RakaDevToolsExtension
{
    private static PipeServer? _server;
    private static CommandRouter? _router;

    /// <summary>
    /// Enables Raka DevTools for this window. Starts a named pipe server
    /// that allows the raka CLI to inspect and modify the visual tree.
    /// </summary>
    /// <param name="window">The main application window.</param>
    /// <returns>The window, for fluent chaining.</returns>
    public static Window UseRakaDevTools(this Window window)
    {
        if (_server != null) return window; // Already initialized

        var pipeName = PipeNames.ForProcess(Environment.ProcessId);
        _router = new CommandRouter();
        _router.SetWindow(window);

        var dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("UseRakaDevTools must be called on the UI thread.");

        _server = new PipeServer(pipeName, _router, dispatcherQueue);
        _server.Start();

        // Log the pipe name so the CLI can discover it
        System.Diagnostics.Debug.WriteLine($"[Raka DevTools] Listening on pipe: {pipeName} (PID: {Environment.ProcessId})");

        return window;
    }
}
