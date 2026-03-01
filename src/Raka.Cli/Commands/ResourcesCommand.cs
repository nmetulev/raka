using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class ResourcesCommand
{
    public static Command Create()
    {
        var scopeOption = new Option<string?>("--scope") { Description = "Resource scope: element, page, app, or all (default: all)" };
        scopeOption.Aliases.Add("-s");
        var filterOption = new Option<string?>("--filter") { Description = "Filter resources by key name (substring match)" };
        filterOption.Aliases.Add("-f");
        var themeOption = new Option<string?>("--theme") { Description = "Filter to specific theme dictionary (Light, Dark, HighContrast)" };
        themeOption.Aliases.Add("-t");
        var elementOption = new Option<string?>("--element") { Description = "Element ID for element-scope resources" };
        elementOption.Aliases.Add("-e");

        var command = new Command("resources", "Browse ResourceDictionary entries at element, page, or app level")
        {
            scopeOption,
            filterOption,
            themeOption,
            elementOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var scope = parseResult.GetValue(scopeOption);
            var filter = parseResult.GetValue(filterOption);
            var theme = parseResult.GetValue(themeOption);
            var element = parseResult.GetValue(elementOption);

            var p = new ResourcesParams(scope, filter, theme, element);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.ResourcesParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.Resources, parameters);
        });

        return command;
    }
}
