using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class SetPropertyCommand
{
    public static Command Create()
    {
        var elementArg = new Argument<string?>("element") { Description = "Element ID (e.g., e5)", Arity = ArgumentArity.ZeroOrOne };
        var propertyArg = new Argument<string>("property") { Description = "Property name (e.g., Margin, Background)" };
        var valueArg = new Argument<string>("value") { Description = "New value (e.g., \"10,20,10,20\", \"#FF0000\")" };
        var nameOption = new Option<string?>("--name") { Description = "Set property on element by x:Name" };
        nameOption.Aliases.Add("-n");

        var command = new Command("set-property", "Set a property value on an element")
        {
            elementArg,
            propertyArg,
            valueArg,
            nameOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var element = parseResult.GetValue(elementArg);
            var property = parseResult.GetValue(propertyArg);
            var value = parseResult.GetValue(valueArg);
            var name = parseResult.GetValue(nameOption);

            if (element == null && name == null)
            {
                Console.Error.WriteLine("Error: Specify element ID or --name");
                Environment.ExitCode = 1;
                return;
            }

            var p = new SetPropertyParams(element, property!, value!, name);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.SetPropertyParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.SetProperty, parameters);
        });

        return command;
    }
}
