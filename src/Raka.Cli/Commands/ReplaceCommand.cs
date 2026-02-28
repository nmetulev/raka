using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class ReplaceCommand
{
    public static Command Create()
    {
        var elementArg = new Argument<string>("element") { Description = "Element ID to replace (e.g., e5)" };
        var xamlArg = new Argument<string>("xaml") { Description = "New XAML to replace with" };

        var command = new Command("replace", "Replace an element in the visual tree with new XAML")
        {
            elementArg,
            xamlArg
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var element = parseResult.GetValue(elementArg);
            var xaml = parseResult.GetValue(xamlArg);

            var p = new ReplaceXamlParams(element!, xaml!);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.ReplaceXamlParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.ReplaceXaml, parameters);
        });

        return command;
    }
}
