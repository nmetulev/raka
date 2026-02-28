using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class AddXamlCommand
{
    public static Command Create()
    {
        var parentArg = new Argument<string>("parent") { Description = "Parent element ID to add into (e.g., e4)" };
        var xamlArg = new Argument<string>("xaml") { Description = "XAML string to inject (e.g., \"<Button Content=\\\"New\\\"/>\")" };
        var indexOption = new Option<int?>("--index", "-i") { Description = "Insert position (0-based). Omit to append." };

        var command = new Command("add-xaml", "Inject XAML into a running app by parsing and adding to a container")
        {
            parentArg,
            xamlArg,
            indexOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var parent = parseResult.GetValue(parentArg);
            var xaml = parseResult.GetValue(xamlArg);
            var index = parseResult.GetValue(indexOption);

            var p = new AddXamlParams(parent!, xaml!, index);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.AddXamlParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.AddXaml, parameters);
        });

        return command;
    }
}
