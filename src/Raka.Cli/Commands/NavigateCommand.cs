using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class NavigateCommand
{
    public static Command Create()
    {
        var pageArg = new Argument<string>("page") { Description = "Page type name (e.g., SettingsPage or MyApp.Pages.SettingsPage)" };

        var command = new Command("navigate", "Navigate a Frame to a specific page type")
        {
            pageArg
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var page = parseResult.GetValue(pageArg)!;
            var parameters = JsonSerializer.SerializeToElement(new NavigateParams(page), CliJsonContext.Default.NavigateParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.Navigate, parameters);
        });

        return command;
    }
}
