using System.CommandLine;
using Raka.Cli.Connection;
using Raka.Cli.Session;
using Raka.Protocol;

namespace Raka.Cli.Commands;

internal static class ConnectCommand
{
    public static Command Create()
    {
        var nameOption = new Option<string?>("--name") { Description = "Process name or window title to connect to" };
        var pidOption = new Option<int?>("--pid") { Description = "Process ID to connect to" };

        var command = new Command("connect", "Connect to a running WinUI 3 app")
        {
            nameOption,
            pidOption
        };

        command.SetAction(async (parseResult) =>
        {
            var name = parseResult.GetValue(nameOption);
            var pid = parseResult.GetValue(pidOption);

            var process = CommandHelpers.FindProcess(name, pid);
            if (process == null)
            {
                Console.Error.WriteLine(name != null
                    ? $"Error: No process found matching '{name}'"
                    : $"Error: No process found with PID {pid}");
                Console.Error.WriteLine("Tip: Make sure the app is running and has Raka.DevTools NuGet added.");
                Environment.ExitCode = 1;
                return;
            }

            var pipeName = PipeNames.ForProcess(process.Id);
            using var client = new PipeClient(pipeName);

            try
            {
                await client.ConnectAsync(5000);
            }
            catch (TimeoutException)
            {
                Console.Error.WriteLine($"Error: Found process '{process.ProcessName}' (PID {process.Id}) but cannot connect.");
                Console.Error.WriteLine("The app may not have Raka.DevTools NuGet added.");
                Console.Error.WriteLine("Add to your app: window.UseRakaDevTools();");
                Environment.ExitCode = 1;
                return;
            }

            // Ping to verify connection
            var response = await client.SendCommandAsync(Raka.Protocol.Commands.Ping);
            if (!response.Success)
            {
                Console.Error.WriteLine($"Error: Connected but ping failed: {response.Error}");
                Environment.ExitCode = 1;
                return;
            }

            // Save session
            SessionManager.SaveActive(new SessionManager.SessionInfo
            {
                PipeName = pipeName,
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                WindowTitle = process.MainWindowTitle,
                ConnectedAt = DateTime.UtcNow
            });

            Console.WriteLine($"Connected to {process.ProcessName} (PID {process.Id})");
            Console.WriteLine($"Pipe: {pipeName}");
        });

        return command;
    }
}
