using System.CommandLine;

namespace Raka.Cli.Commands;

internal static class ListPagesCommand
{
    public static Command Create()
    {
        var command = new Command("list-pages", "List all page types available for navigation");
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.ListPages, null);
        });

        return command;
    }
}
