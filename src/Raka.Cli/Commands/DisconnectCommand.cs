using System.CommandLine;
using Raka.Cli.Session;

namespace Raka.Cli.Commands;

internal static class DisconnectCommand
{
    public static Command Create()
    {
        var command = new Command("disconnect", "Disconnect from the current app");

        command.SetAction((parseResult) =>
        {
            var session = SessionManager.LoadActive();
            if (session == null)
            {
                Console.WriteLine("No active connection to disconnect.");
                return;
            }

            SessionManager.ClearActive();
            Console.WriteLine($"Disconnected from {session.ProcessName} (PID {session.ProcessId})");
        });

        return command;
    }
}
