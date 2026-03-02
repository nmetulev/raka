using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class TypeCommand
{
    public static Command Create()
    {
        var textArg = new Argument<string>("text") { Description = "Text to type into the element" };
        var elementOption = new Option<string?>("--element") { Description = "Element ID (e.g., e5)" };
        elementOption.Aliases.Add("-e");
        var nameOption = new Option<string?>("--name") { Description = "Target element by x:Name" };
        nameOption.Aliases.Add("-n");
        var delayOption = new Option<int?>("--delay") { Description = "Inter-key delay in ms (default: 30)" };
        delayOption.Aliases.Add("-d");

        var command = new Command("type", "Type text via real keystroke simulation (triggers TextChanged, dropdown suggestions, etc.)")
        {
            textArg,
            elementOption,
            nameOption,
            delayOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var text = parseResult.GetValue(textArg);
            var element = parseResult.GetValue(elementOption);
            var name = parseResult.GetValue(nameOption);
            var delay = parseResult.GetValue(delayOption);

            if (element == null && name == null)
            {
                Console.Error.WriteLine("Error: Specify --element or --name");
                Environment.ExitCode = 1;
                return;
            }

            var p = new TypeParams(text!, element, name, delay);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.TypeParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.Type, parameters);
        });

        return command;
    }
}
