using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class HotkeyCommand
{
    public static Command Create()
    {
        var keysArg = new Argument<string>("keys") { Description = "Key combination (e.g., Ctrl+S, Alt+F4, Tab, Shift+Tab, Enter)" };

        var command = new Command("hotkey", "Send a keyboard shortcut via OS input simulation")
        {
            keysArg
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var keys = parseResult.GetValue(keysArg);
            if (string.IsNullOrWhiteSpace(keys))
            {
                Console.Error.WriteLine("Error: Specify a key combination (e.g., Ctrl+S)");
                Environment.ExitCode = 1;
                return;
            }

            var p = new HotkeyParams(keys!);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.HotkeyParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.Hotkey, parameters);
        });

        return command;
    }
}
