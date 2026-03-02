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
        var classOption = new Option<string?>("--class") { Description = "Search by full class name (e.g., Microsoft.UI.Xaml.Controls.NavigationViewItem)" };
        var interactiveOption = new Option<bool>("--interactive") { Description = "Only return interactive elements (clickable, toggleable, or selectable)" };
        var visibleOption = new Option<bool>("--visible") { Description = "Only return visible elements (Visibility=Visible)" };
        var propertyOption = new Option<string?>("--property") { Description = "Filter by property value (e.g., Tag=dashboard, IsEnabled=True)" };
        var fromPageOption = new Option<bool>("-p") { Description = "Scope to current page content (skip framework nesting)" };
        fromPageOption.Aliases.Add("--from-page");

        var command = new Command("search", "Search for elements in the visual tree")
        {
            typeOption,
            nameOption,
            textOption,
            autoIdOption,
            classOption,
            interactiveOption,
            visibleOption,
            propertyOption,
            fromPageOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var type = parseResult.GetValue(typeOption);
            var name = parseResult.GetValue(nameOption);
            var text = parseResult.GetValue(textOption);
            var autoId = parseResult.GetValue(autoIdOption);
            var className = parseResult.GetValue(classOption);
            var interactive = parseResult.GetValue(interactiveOption);
            var visibleOnly = parseResult.GetValue(visibleOption);
            var property = parseResult.GetValue(propertyOption);
            var fromPage = parseResult.GetValue(fromPageOption);

            if (type == null && name == null && text == null && autoId == null && className == null && !interactive && !visibleOnly && property == null)
            {
                Console.Error.WriteLine("Error: Specify at least one search criterion (--type, --name, --text, --automation-id, --class, --interactive, --visible, --property)");
                Environment.ExitCode = 1;
                return;
            }

            var p = new SearchParams(type, name, text, autoId, className,
                interactive ? true : null, visibleOnly ? true : null, property, fromPage ? true : null);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.SearchParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.Search, parameters);
        });

        return command;
    }
}
