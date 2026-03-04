using System.CommandLine;
using Raka.Cli.Session;

namespace Raka.Cli.Commands;

internal static class ListCommand
{
    public static Command Create()
    {
        var command = new Command("list", "List active connections");

        command.SetAction((parseResult) =>
        {
            var session = SessionManager.LoadActive();
            if (session == null)
            {
                Console.WriteLine("No active connections.");
                Console.WriteLine("Use 'raka status --app <AppName>' to connect to a running app.");
                return;
            }

            Console.WriteLine($"Active connection:");
            Console.WriteLine($"  Process: {session.ProcessName} (PID {session.ProcessId})");
            Console.WriteLine($"  Window:  {session.WindowTitle}");
            Console.WriteLine($"  Pipe:    {session.PipeName}");
            Console.WriteLine($"  Since:   {session.ConnectedAt:u}");
        });

        return command;
    }
}
