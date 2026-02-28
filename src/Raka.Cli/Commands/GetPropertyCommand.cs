using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class GetPropertyCommand
{
    public static Command Create()
    {
        var elementArg = new Argument<string>("element") { Description = "Element ID (e.g., e5)" };
        var propertyArg = new Argument<string?>("property") { Description = "Property name (e.g., Background, Margin)", Arity = ArgumentArity.ZeroOrOne };
        var allOption = new Option<bool>("-a") { Description = "List all properties" };
        allOption.Aliases.Add("--all");

        var command = new Command("get-property", "Read property values from an element")
        {
            elementArg,
            propertyArg,
            allOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var element = parseResult.GetValue(elementArg);
            var property = parseResult.GetValue(propertyArg);
            var all = parseResult.GetValue(allOption);

            if (!all && property == null)
            {
                Console.Error.WriteLine("Error: Specify a property name or use --all");
                Environment.ExitCode = 1;
                return;
            }

            var p = new GetPropertyParams(element!, property, all ? true : null);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.GetPropertyParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.GetProperty, parameters);
        });

        return command;
    }
}
