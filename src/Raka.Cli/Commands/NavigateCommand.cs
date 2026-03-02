using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class NavigateCommand
{
    public static Command Create()
    {
        var pageArg = new Argument<string>("page") { Description = "Page type name (e.g., SettingsPage or MyApp.Pages.SettingsPage)" };
        var paramOption = new Option<string?>("--param") { Description = "Navigation parameter (string value passed to Page.OnNavigatedTo)" };

        var command = new Command("navigate", "Navigate a Frame to a specific page type")
        {
            pageArg,
            paramOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var page = parseResult.GetValue(pageArg)!;
            var param = parseResult.GetValue(paramOption);
            var parameters = JsonSerializer.SerializeToElement(new NavigateParams(page, param), CliJsonContext.Default.NavigateParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.Navigate, parameters);
        });

        return command;
    }
}
