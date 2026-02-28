using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class BatchCommand
{
    public static Command Create()
    {
        var commandsArg = new Argument<string[]>("commands") { Description = "Commands to execute (e.g., \"click e1\" \"screenshot -f out.png\")", Arity = ArgumentArity.OneOrMore };

        var command = new Command("batch", "Execute multiple commands in sequence over a single connection")
        {
            commandsArg
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var commands = parseResult.GetValue(commandsArg)!;

            using var client = await CommandHelpers.GetConnectedClient(parseResult);

            var results = new List<(string cmd, bool success, JsonElement? data, string? error)>();
            int failures = 0;

            foreach (var cmd in commands)
            {
                var (protocolCommand, parameters) = ParseSubCommand(cmd);
                if (protocolCommand == null)
                {
                    results.Add((cmd, false, null, "Unknown command"));
                    failures++;
                    continue;
                }

                var response = await client.SendCommandAsync(protocolCommand, parameters);
                if (response.Success)
                {
                    results.Add((cmd, true, response.Data, (string?)null));
                }
                else
                {
                    results.Add((cmd, false, null, response.Error));
                    failures++;
                }
            }

            // Build output JSON manually (AOT-safe)
            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("total", commands.Length);
                writer.WriteNumber("succeeded", commands.Length - failures);
                writer.WriteNumber("failed", failures);
                writer.WriteStartArray("results");
                foreach (var (cmd, success, data, error) in results)
                {
                    writer.WriteStartObject();
                    writer.WriteString("command", cmd);
                    writer.WriteBoolean("success", success);
                    if (data.HasValue)
                    {
                        writer.WritePropertyName("data");
                        data.Value.WriteTo(writer);
                    }
                    if (error != null)
                    {
                        writer.WriteString("error", error);
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
            Environment.ExitCode = failures > 0 ? 1 : 0;
        });

        return command;
    }

    private static (string? command, JsonElement? parameters) ParseSubCommand(string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return (null, null);

        var cmd = parts[0].ToLowerInvariant();
        return cmd switch
        {
            "click" when parts.Length >= 2 => (Raka.Protocol.Commands.Click, Elem("element", parts[1])),
            "screenshot" => ParseScreenshotArgs(parts),
            "inspect" => ParseInspectArgs(parts),
            "search" => ParseSearchArgs(parts),
            "status" => (Raka.Protocol.Commands.Status, null),
            "navigate" when parts.Length >= 2 => (Raka.Protocol.Commands.Navigate, Elem("page", parts[1])),
            "get-property" when parts.Length >= 3 => (Raka.Protocol.Commands.GetProperty, Build(("element", parts[1]), ("property", parts[2]))),
            "set-property" when parts.Length >= 4 => (Raka.Protocol.Commands.SetProperty, Build(("element", parts[1]), ("property", parts[2]), ("value", parts[3]))),
            _ => (null, null)
        };
    }

    private static (string?, JsonElement?) ParseScreenshotArgs(string[] parts)
    {
        string? element = null, mode = null, bg = null;
        for (int i = 1; i < parts.Length; i++)
        {
            switch (parts[i])
            {
                case "--mode" when i + 1 < parts.Length: mode = parts[++i]; break;
                case "--bg" when i + 1 < parts.Length: bg = parts[++i]; break;
                case "-f" or "--filename" when i + 1 < parts.Length: i++; break; // skip filename in batch
                default:
                    if (element == null && parts[i].StartsWith('e')) element = parts[i];
                    break;
            }
        }
        return (Raka.Protocol.Commands.Screenshot, Build(("element", element), ("mode", mode), ("background", bg)));
    }

    private static (string?, JsonElement?) ParseInspectArgs(string[] parts)
    {
        string? element = null, depth = null;
        for (int i = 1; i < parts.Length; i++)
        {
            switch (parts[i])
            {
                case "-e" when i + 1 < parts.Length: element = parts[++i]; break;
                case "-d" when i + 1 < parts.Length: depth = parts[++i]; break;
                default:
                    if (element == null && parts[i].StartsWith('e')) element = parts[i];
                    break;
            }
        }
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (element != null) writer.WriteString("element", element);
            if (depth != null && int.TryParse(depth, out var d)) writer.WriteNumber("depth", d);
            writer.WriteEndObject();
        }
        using var doc = JsonDocument.Parse(stream.ToArray());
        return (Raka.Protocol.Commands.Inspect, doc.RootElement.Clone());
    }

    private static (string?, JsonElement?) ParseSearchArgs(string[] parts)
    {
        string? type = null, name = null, text = null;
        for (int i = 1; i < parts.Length; i++)
        {
            switch (parts[i])
            {
                case "-t" or "--type" when i + 1 < parts.Length: type = parts[++i]; break;
                case "-n" or "--name" when i + 1 < parts.Length: name = parts[++i]; break;
                case "--text" when i + 1 < parts.Length: text = parts[++i]; break;
            }
        }
        return (Raka.Protocol.Commands.Search, Build(("type", type), ("name", name), ("text", text)));
    }

    /// <summary>Build a single-property JsonElement.</summary>
    private static JsonElement Elem(string key, string value)
    {
        using var doc = JsonDocument.Parse($"{{\"{key}\":\"{value}\"}}");
        return doc.RootElement.Clone();
    }

    /// <summary>Build a JsonElement from key-value pairs (nulls are skipped).</summary>
    private static JsonElement Build(params (string key, string? value)[] pairs)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in pairs)
            {
                if (value != null)
                {
                    writer.WriteString(key, value);
                }
            }
            writer.WriteEndObject();
        }
        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }
}
