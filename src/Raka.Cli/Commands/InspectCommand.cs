using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class InspectCommand
{
    public static Command Create()
    {
        var elementOption = new Option<string?>("-e") { Description = "Element ID to inspect (e.g., e5). Omit for full tree." };
        elementOption.Aliases.Add("--element");
        var depthOption = new Option<int?>("-d") { Description = "Maximum depth to traverse" };
        depthOption.Aliases.Add("--depth");
        var formatOption = new Option<string?>("--format") { Description = "Output format: 'json' (default) or 'tree' (ASCII tree view)" };
        formatOption.Aliases.Add("-f");

        var command = new Command("inspect", "Inspect the visual tree of the connected app")
        {
            elementOption,
            depthOption,
            formatOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var element = parseResult.GetValue(elementOption);
            var depth = parseResult.GetValue(depthOption);
            var format = parseResult.GetValue(formatOption);

            var p = new InspectParams(element, depth);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.InspectParams);

            if (string.Equals(format, "tree", StringComparison.OrdinalIgnoreCase))
            {
                using var client = await CommandHelpers.GetConnectedClient(parseResult);
                var response = await client.SendCommandAsync(Raka.Protocol.Commands.Inspect, parameters);
                if (!response.Success)
                {
                    Console.Error.WriteLine($"Error: {response.Error}");
                    Environment.ExitCode = 1;
                    return;
                }
                if (response.Data.HasValue)
                {
                    PrintTree(response.Data.Value, "", true);
                }
            }
            else
            {
                Environment.ExitCode = await CommandHelpers.SendAndPrint(
                    parseResult,
                    Raka.Protocol.Commands.Inspect,
                    parameters);
            }
        });

        return command;
    }

    private static void PrintTree(JsonElement node, string indent, bool isLast)
    {
        var connector = indent.Length == 0 ? "" : (isLast ? "└─ " : "├─ ");
        var id = node.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        var type = node.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : "?";
        var name = node.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
        var contentClass = node.TryGetProperty("contentClassName", out var ccProp) ? ccProp.GetString() : null;

        var label = $"{type}";
        if (name != null) label += $" #{name}";
        if (id != null) label += $" ({id})";
        if (contentClass != null) label += $" → {contentClass}";
        var src = node.TryGetProperty("sourceFile", out var srcProp) ? srcProp.GetString() : null;
        if (src != null) label += $" [{src}]";

        Console.WriteLine($"{indent}{connector}{label}");

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            var childIndent = indent + (indent.Length == 0 ? "" : (isLast ? "   " : "│  "));
            var arr = children.EnumerateArray().ToArray();
            for (int i = 0; i < arr.Length; i++)
            {
                PrintTree(arr[i], childIndent, i == arr.Length - 1);
            }
        }
    }
}
