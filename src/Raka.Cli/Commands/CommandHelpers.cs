using System.Diagnostics;
using System.Text.Json;
using Raka.Cli.Connection;
using Raka.Cli.Session;
using Raka.Protocol;

namespace Raka.Cli.Commands;

/// <summary>
/// Shared utilities for CLI commands.
/// </summary>
internal static class CommandHelpers
{
    /// <summary>
    /// Gets a connected pipe client using the active session.
    /// </summary>
    public static async Task<PipeClient> GetConnectedClient()
    {
        var session = SessionManager.LoadActive()
            ?? throw new InvalidOperationException(
                "No active connection. Run 'raka connect --name <AppName>' first.");

        var client = new PipeClient(session.PipeName);
        try
        {
            await client.ConnectAsync(3000);
        }
        catch (TimeoutException)
        {
            SessionManager.ClearActive();
            throw new InvalidOperationException(
                $"Cannot connect to {session.ProcessName ?? "app"} (PID {session.ProcessId}). " +
                "The app may have closed. Run 'raka connect' again.");
        }
        return client;
    }

    /// <summary>
    /// Sends a command and prints the result as JSON.
    /// </summary>
    public static async Task<int> SendAndPrint(string command, object? parameters = null)
    {
        using var client = await GetConnectedClient();
        var response = await client.SendCommandAsync(command, parameters);

        if (!response.Success)
        {
            Console.Error.WriteLine($"Error: {response.Error}");
            return 1;
        }

        if (response.Data.HasValue)
        {
            Console.WriteLine(JsonSerializer.Serialize(response.Data.Value, RakaJson.PrettyOptions));
        }

        return 0;
    }

    /// <summary>
    /// Finds a process by name, PID, or window title.
    /// </summary>
    public static Process? FindProcess(string? name = null, int? pid = null)
    {
        if (pid.HasValue)
        {
            try { return Process.GetProcessById(pid.Value); }
            catch { return null; }
        }

        if (name != null)
        {
            // Try exact process name match first
            var procs = Process.GetProcessesByName(name);
            if (procs.Length > 0) return procs[0];

            // Try partial match on process name or window title
            var all = Process.GetProcesses();
            foreach (var p in all)
            {
                try
                {
                    if (p.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase))
                        return p;
                    if (!string.IsNullOrEmpty(p.MainWindowTitle) &&
                        p.MainWindowTitle.Contains(name, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
                catch { }
            }
        }

        return null;
    }
}
