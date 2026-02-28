using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class ClickCommand
{
    public static Command Create()
    {
        var elementArg = new Argument<string>("element") { Description = "Element ID to click (e.g., e5)" };

        var command = new Command("click", "Click a button, toggle a checkbox, or select an item")
        {
            elementArg
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var element = parseResult.GetValue(elementArg);
            var parameters = JsonSerializer.SerializeToElement(new ElementParams(element!), CliJsonContext.Default.ElementParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.Click, parameters);
        });

        return command;
    }
}
