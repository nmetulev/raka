using System.CommandLine;
using Raka.Cli.Commands;

var rootCommand = new RootCommand("raka — WinUI 3 app automation tool for AI agents and developers")
{
    ConnectCommand.Create(),
    InspectCommand.Create(),
    SearchCommand.Create(),
    GetPropertyCommand.Create(),
    SetPropertyCommand.Create(),
    ClickCommand.Create(),
    InvokeCommand.Create(),
    ScreenshotCommand.Create(),
    AddXamlCommand.Create(),
    RemoveCommand.Create(),
    ReplaceCommand.Create(),
    AncestorsCommand.Create(),
    ListCommand.Create(),
    TapInspectCommand.Create(),
    HotReloadCommand.Create(),
    StatusCommand.Create(),
    NavigateCommand.Create(),
    ListPagesCommand.Create(),
    TypeCommand.Create(),
    HotkeyCommand.Create(),
    GetStatesCommand.Create(),
    SetStateCommand.Create(),
    StylesCommand.Create(),
    ResourcesCommand.Create(),
    SetResourceCommand.Create(),
    BatchCommand.Create(),
    DisconnectCommand.Create(),
};

return await rootCommand.Parse(args).InvokeAsync();
