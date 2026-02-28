using System.CommandLine;
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
    /// Global options that every command can use to target an app.
    /// If not provided, falls back to the saved session.
    /// </summary>
    public static Option<string?> AppOption { get; } = new("--app") { Description = "Target app by process name or window title" };
    public static Option<int?> PidOption { get; } = new("--pid") { Description = "Target app by process ID" };

    /// <summary>
    /// Adds --name and --pid options to a command.
    /// </summary>
    public static void AddTargetOptions(Command command)
    {
        command.Add(AppOption);
        command.Add(PidOption);
    }

    /// <summary>
    /// Resolves the target app: uses --name/--pid if provided, otherwise falls back to saved session.
    /// Opens a fresh pipe connection each time (no long-running process needed).
    /// </summary>
    public static async Task<PipeClient> GetConnectedClient(ParseResult parseResult)
    {
        var name = parseResult.GetValue(AppOption);
        var pid = parseResult.GetValue(PidOption);

        string pipeName;
        string processLabel;

        if (name != null || pid != null)
        {
            // Explicit target — resolve process and connect
            var process = FindProcess(name, pid)
                ?? throw new InvalidOperationException(
                    name != null
                        ? $"No process found matching '{name}'. Is the app running?"
                        : $"No process found with PID {pid}.");

            pipeName = PipeNames.ForProcess(process.Id);
            processLabel = $"{process.ProcessName} (PID {process.Id})";

            // Save as active session for convenience
            SessionManager.SaveActive(new SessionManager.SessionInfo
            {
                PipeName = pipeName,
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                WindowTitle = process.MainWindowTitle,
                ConnectedAt = DateTime.UtcNow
            });
        }
        else
        {
            // No explicit target — use saved session
            var session = SessionManager.LoadActive()
                ?? throw new InvalidOperationException(
                    "No target app specified. Use --name <AppName> or --pid <PID>.\n" +
                    "Example: raka inspect --name MyApp");

            pipeName = session.PipeName;
            processLabel = $"{session.ProcessName ?? "app"} (PID {session.ProcessId})";
        }

        var client = new PipeClient(pipeName);
        try
        {
            await client.ConnectAsync(3000);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                $"Cannot connect to {processLabel}. " +
                "Make sure the app has Raka.DevTools NuGet added:\n" +
                "  window.UseRakaDevTools();");
        }
        return client;
    }

    /// <summary>
    /// Sends a command and prints the result as JSON.
    /// </summary>
    public static async Task<int> SendAndPrint(ParseResult parseResult, string command, object? parameters = null)
    {
        using var client = await GetConnectedClient(parseResult);
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
            var procs = Process.GetProcessesByName(name);
            if (procs.Length > 0) return procs[0];

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
