using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class ScrollIntoViewCommand
{
    public static Command Create()
    {
        var elementArg = new Argument<string?>("element") { Description = "Element ID to scroll into view (e.g., e5)", Arity = ArgumentArity.ZeroOrOne };
        var nameOption = new Option<string?>("--name") { Description = "Target element by x:Name" };
        nameOption.Aliases.Add("-n");
        var typeOption = new Option<string?>("--type") { Description = "Target element by type (combine with --text)" };
        typeOption.Aliases.Add("-t");
        var textOption = new Option<string?>("--text") { Description = "Filter by text content (use with --type)" };

        var command = new Command("scroll-into-view", "Scroll an element into the visible area of its ScrollViewer")
        {
            elementArg,
            nameOption,
            typeOption,
            textOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var element = parseResult.GetValue(elementArg);
            var name = parseResult.GetValue(nameOption);
            var type = parseResult.GetValue(typeOption);
            var text = parseResult.GetValue(textOption);

            if (element == null && name == null && type == null)
            {
                Console.Error.WriteLine("Error: Specify element ID, --name, or --type");
                Environment.ExitCode = 1;
                return;
            }

            var p = new ScrollIntoViewParams(element, name, type, text);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.ScrollIntoViewParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.ScrollIntoView, parameters);
        });

        return command;
    }
}
