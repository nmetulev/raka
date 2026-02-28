using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class AncestorsCommand
{
    public static Command Create()
    {
        var elementArg = new Argument<string>("element") { Description = "Element ID (e.g., e5)" };

        var command = new Command("ancestors", "Show the parent chain from an element to the root")
        {
            elementArg
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var element = parseResult.GetValue(elementArg);
            var parameters = JsonSerializer.SerializeToElement(new ElementParams(element!), CliJsonContext.Default.ElementParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.Ancestors, parameters);
        });

        return command;
    }
}
