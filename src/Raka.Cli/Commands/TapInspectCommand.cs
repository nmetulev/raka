using System.CommandLine;
using Raka.Cli.Connection;

namespace Raka.Cli.Commands;

/// <summary>
/// Inspects a WinUI 3 app's visual tree using TAP DLL injection.
/// Works WITHOUT the Raka.DevTools NuGet package — targets any WinUI 3 app.
/// </summary>
internal sealed class TapInspectCommand
{
    public static Command Create()
    {
        var command = new Command("tap-inspect", "Inspect any WinUI 3 app via TAP DLL injection (no NuGet required)");
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult, ct) =>
        {
            var name = parseResult.GetValue(CommandHelpers.AppOption);
            var pid = parseResult.GetValue(CommandHelpers.PidOption);

            var process = CommandHelpers.FindProcess(name, pid);
            if (process == null)
            {
                Console.Error.WriteLine(name != null
                    ? $"No process found matching '{name}'. Is the app running?"
                    : pid.HasValue
                        ? $"No process found with PID {pid}."
                        : "No target app specified. Use --app <AppName> or --pid <PID>.");
                return;
            }

            Console.Error.WriteLine($"Injecting TAP DLL into {process.ProcessName} (PID {process.Id})...");

            try
            {
                var json = await TapInjector.InjectAndReadTreeAsync(process.Id);
                Console.WriteLine(json);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
        });

        return command;
    }
}
