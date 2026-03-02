using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class GetStatesCommand
{
    public static Command Create()
    {
        var elementArg = new Argument<string?>("element") { Description = "Element ID (e.g., e5)", Arity = ArgumentArity.ZeroOrOne };
        var nameOption = new Option<string?>("--name") { Description = "Target element by x:Name" };
        nameOption.Aliases.Add("-n");

        var command = new Command("get-states", "List visual state groups and current states for an element")
        {
            elementArg,
            nameOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var element = parseResult.GetValue(elementArg);
            var name = parseResult.GetValue(nameOption);

            if (element == null && name == null)
            {
                Console.Error.WriteLine("Error: Specify element ID or --name");
                Environment.ExitCode = 1;
                return;
            }

            var p = new GetStatesParams(element, name);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.GetStatesParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.GetStates, parameters);
        });

        return command;
    }
}
