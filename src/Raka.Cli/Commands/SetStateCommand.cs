using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class SetStateCommand
{
    public static Command Create()
    {
        var elementArg = new Argument<string?>("element") { Description = "Element ID (e.g., e5)", Arity = ArgumentArity.ZeroOrOne };
        var nameOption = new Option<string?>("--name") { Description = "Target element by x:Name" };
        nameOption.Aliases.Add("-n");
        var stateOption = new Option<string>("--state") { Description = "Visual state name to apply (e.g., PointerOver, Pressed)" };
        stateOption.Aliases.Add("-s");
        stateOption.Required = true;
        var groupOption = new Option<string?>("--group") { Description = "Visual state group name (auto-detected if omitted)" };
        groupOption.Aliases.Add("-g");

        var command = new Command("set-state", "Set visual state on an element via VisualStateManager")
        {
            elementArg,
            nameOption,
            stateOption,
            groupOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var element = parseResult.GetValue(elementArg);
            var name = parseResult.GetValue(nameOption);
            var state = parseResult.GetValue(stateOption);
            var group = parseResult.GetValue(groupOption);

            if (element == null && name == null)
            {
                Console.Error.WriteLine("Error: Specify element ID or --name");
                Environment.ExitCode = 1;
                return;
            }

            var p = new SetStateParams(element, name, state!, group);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.SetStateParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.SetState, parameters);
        });

        return command;
    }
}
