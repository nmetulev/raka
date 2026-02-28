using System.CommandLine;
using Raka.Cli.Session;

namespace Raka.Cli.Commands;

internal static class ConnectCommand
{
    public static Command Create()
    {
        var command = new Command("connect", "Test connection to a running WinUI 3 app and save as default target");
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            try
            {
                using var client = await CommandHelpers.GetConnectedClient(parseResult);
                var response = await client.SendCommandAsync(Raka.Protocol.Commands.Ping);

                if (!response.Success)
                {
                    Console.Error.WriteLine($"Error: Connected but ping failed: {response.Error}");
                    Environment.ExitCode = 1;
                    return;
                }

                var session = SessionManager.LoadActive();
                Console.WriteLine($"Connected to {session?.ProcessName} (PID {session?.ProcessId})");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        });

        return command;
    }
}
