using System.CommandLine;
using Raka.Protocol;

namespace Raka.Cli.Commands;

internal static class SetPropertyCommand
{
    public static Command Create()
    {
        var elementArg = new Argument<string>("element") { Description = "Element ID (e.g., e5)" };
        var propertyArg = new Argument<string>("property") { Description = "Property name (e.g., Margin, Background)" };
        var valueArg = new Argument<string>("value") { Description = "New value (e.g., \"10,20,10,20\", \"#FF0000\")" };

        var command = new Command("set-property", "Set a property value on an element")
        {
            elementArg,
            propertyArg,
            valueArg
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var element = parseResult.GetValue(elementArg);
            var property = parseResult.GetValue(propertyArg);
            var value = parseResult.GetValue(valueArg);

            var parameters = new Dictionary<string, object>
            {
                ["element"] = element!,
                ["property"] = property!,
                ["value"] = value!
            };

            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.SetProperty, parameters);
        });

        return command;
    }
}
