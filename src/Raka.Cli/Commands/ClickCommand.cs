using System.CommandLine;

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
            var parameters = new Dictionary<string, object> { ["element"] = element! };
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.Click, parameters);
        });

        return command;
    }
}
