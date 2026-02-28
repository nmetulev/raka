using System.CommandLine;
using Raka.Protocol;

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

        command.SetAction(async (parseResult) =>
        {
            var element = parseResult.GetValue(elementOption);
            var depth = parseResult.GetValue(depthOption);

            var parameters = new Dictionary<string, object>();
            if (element != null) parameters["element"] = element;
            if (depth.HasValue) parameters["depth"] = depth.Value;

            Environment.ExitCode = await CommandHelpers.SendAndPrint(
                Raka.Protocol.Commands.Inspect,
                parameters.Count > 0 ? parameters : null);
        });

        return command;
    }
}
