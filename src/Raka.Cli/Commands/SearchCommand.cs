using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class SearchCommand
{
    public static Command Create()
    {
        var typeOption = new Option<string?>("-t") { Description = "Search by element type (e.g., Button, TextBlock)" };
        typeOption.Aliases.Add("--type");
        var nameOption = new Option<string?>("-n") { Description = "Search by x:Name" };
        nameOption.Aliases.Add("--name");
        var textOption = new Option<string?>("--text") { Description = "Search by text content" };
        var autoIdOption = new Option<string?>("--automation-id") { Description = "Search by AutomationId" };

        var command = new Command("search", "Search for elements in the visual tree")
        {
            typeOption,
            nameOption,
            textOption,
            autoIdOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var type = parseResult.GetValue(typeOption);
            var name = parseResult.GetValue(nameOption);
            var text = parseResult.GetValue(textOption);
            var autoId = parseResult.GetValue(autoIdOption);

            if (type == null && name == null && text == null && autoId == null)
            {
                Console.Error.WriteLine("Error: Specify at least one search criterion (--type, --name, --text, --automation-id)");
                Environment.ExitCode = 1;
                return;
            }

            var p = new SearchParams(type, name, text, autoId);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.SearchParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.Search, parameters);
        });

        return command;
    }
}
