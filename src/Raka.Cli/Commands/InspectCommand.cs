using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class InspectCommand
{
    public static Command Create()
    {
        var elementOption = new Option<string?>("-e") { Description = "Element ID to inspect (e.g., e5). Omit for full tree." };
        elementOption.Aliases.Add("--element");
        var depthOption = new Option<int?>("-d") { Description = "Maximum depth to traverse" };
        depthOption.Aliases.Add("--depth");

        var command = new Command("inspect", "Inspect the visual tree of the connected app")
        {
            elementOption,
            depthOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var element = parseResult.GetValue(elementOption);
            var depth = parseResult.GetValue(depthOption);

            var p = new InspectParams(element, depth);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.InspectParams);

            Environment.ExitCode = await CommandHelpers.SendAndPrint(
                parseResult,
                Raka.Protocol.Commands.Inspect,
                parameters);
        });

        return command;
    }
}
