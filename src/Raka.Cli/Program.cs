using System.CommandLine;
using Raka.Cli.Commands;

var rootCommand = new RootCommand("raka — WinUI 3 app automation tool for AI agents and developers")
{
    ConnectCommand.Create(),
    InspectCommand.Create(),
    SearchCommand.Create(),
    GetPropertyCommand.Create(),
    SetPropertyCommand.Create(),
    ScreenshotCommand.Create(),
    AncestorsCommand.Create(),
    ListCommand.Create(),
    DisconnectCommand.Create(),
};

return await rootCommand.Parse(args).InvokeAsync();
