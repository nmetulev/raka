using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class RemoveCommand
{
    public static Command Create()
    {
        var elementArg = new Argument<string>("element") { Description = "Element ID to remove (e.g., e5)" };

        var command = new Command("remove", "Remove an element from the visual tree")
        {
            elementArg
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var element = parseResult.GetValue(elementArg);
            var parameters = JsonSerializer.SerializeToElement(new ElementParams(element!), CliJsonContext.Default.ElementParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.RemoveElement, parameters);
        });

        return command;
    }
}
