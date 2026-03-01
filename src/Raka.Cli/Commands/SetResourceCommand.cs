using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class SetResourceCommand
{
    public static Command Create()
    {
        var keyArg = new Argument<string>("key") { Description = "Resource key (e.g., TextFillColorPrimary, ButtonBackground)" };
        var valueArg = new Argument<string>("value") { Description = "New value (e.g., #FF0000, 8, true)" };
        var scopeOption = new Option<string?>("--scope") { Description = "Target scope: page or app (default: auto-detect)" };
        scopeOption.Aliases.Add("-s");

        var command = new Command("set-resource", "Modify a resource value at runtime (propagates to all consumers)")
        {
            keyArg,
            valueArg,
            scopeOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var key = parseResult.GetValue(keyArg);
            var value = parseResult.GetValue(valueArg);
            var scope = parseResult.GetValue(scopeOption);

            var p = new SetResourceParams(key!, value!, scope);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.SetResourceParams);
            Environment.ExitCode = await CommandHelpers.SendAndPrint(parseResult, Raka.Protocol.Commands.SetResource, parameters);
        });

        return command;
    }
}
