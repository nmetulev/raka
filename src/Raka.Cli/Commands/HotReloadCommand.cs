using System.CommandLine;
using System.Text.Json;
using Raka.Protocol;

namespace Raka.Cli.Commands;

/// <summary>
/// Watches a XAML file and hot-reloads the running app on each save.
/// Uses XamlReader.Load() via the NuGet pipe to parse and replace content.
/// </summary>
internal static class HotReloadCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "XAML file to watch (e.g., MainPage.xaml)" };
        var elementOption = new Option<string?>("--element", "-e") { Description = "Element ID to replace (e.g., e6)" };
        var nameOption = new Option<string?>("--target-name", "-n") { Description = "Target element by x:Name (e.g., MainPanel)" };

        var command = new Command("hot-reload", "Watch a XAML file and hot-reload the running app on save")
        {
            fileArg,
            elementOption,
            nameOption,
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult, ct) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var elementId = parseResult.GetValue(elementOption);
            var targetName = parseResult.GetValue(nameOption);

            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"File not found: {file}");
                Environment.ExitCode = 1;
                return;
            }

            var fullPath = Path.GetFullPath(file);
            Console.Error.WriteLine($"Watching: {fullPath}");

            // Resolve target element
            if (elementId == null && targetName != null)
            {
                // Find element by x:Name via inspect
                elementId = await FindElementByName(parseResult, targetName);
                if (elementId == null)
                {
                    Console.Error.WriteLine($"No element found with x:Name '{targetName}'.");
                    Environment.ExitCode = 1;
                    return;
                }
            }
            else if (elementId == null)
            {
                elementId = await AutoDetectElement(parseResult, fullPath);
                if (elementId == null)
                {
                    Console.Error.WriteLine("Could not auto-detect target element. Use --element <id> or --target-name <name>.");
                    Environment.ExitCode = 1;
                    return;
                }
            }
            Console.Error.WriteLine($"Target element: {elementId}");

            // Do initial reload
            // Since inspect re-walks the tree and reassigns IDs from e0,
            // the element at the same position keeps its original ID.
            await ReloadXaml(parseResult, fullPath, elementId);

            // Watch for changes
            var dir = Path.GetDirectoryName(fullPath)!;
            var fileName = Path.GetFileName(fullPath);
            using var watcher = new FileSystemWatcher(dir, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            var debounce = DateTime.MinValue;
            var tcs = new TaskCompletionSource();

            watcher.Changed += async (_, e) =>
            {
                // Debounce: ignore rapid-fire events within 300ms
                var now = DateTime.UtcNow;
                if ((now - debounce).TotalMilliseconds < 300) return;
                debounce = now;

                // Small delay to let the file finish writing
                await Task.Delay(100);

                await ReloadXaml(parseResult, fullPath, elementId);
            };

            Console.Error.WriteLine("Hot reload active. Save the file to reload. Press Ctrl+C to stop.");

            // Wait for cancellation
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                tcs.TrySetResult();
            };

            await tcs.Task;
            Console.Error.WriteLine("Hot reload stopped.");
        });

        return command;
    }

    /// <summary>
    /// Reloads the XAML file content into the running app.
    /// </summary>
    private static async Task ReloadXaml(ParseResult parseResult, string filePath, string elementId)
    {
        try
        {
            var xaml = await File.ReadAllTextAsync(filePath);

            // Extract inner content from root element for replacement.
            // If the file has a Page/UserControl root, we want to replace the target
            // element with the root's content (not wrap it in another Page).
            var innerXaml = ExtractInnerContent(xaml);

            using var client = await CommandHelpers.GetConnectedClient(parseResult);

            // Ensure the tree walker is populated — replace needs elements indexed.
            // inspect resets IDs from e0, so the element at the same position keeps its ID.
            await client.SendCommandAsync(Protocol.Commands.Inspect, null);

            var p = new ReplaceXamlParams(elementId, innerXaml);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.ReplaceXamlParams);
            var response = await client.SendCommandAsync(Protocol.Commands.ReplaceXaml, parameters);

            if (response.Success)
            {
                var time = DateTime.Now.ToString("HH:mm:ss");
                Console.Error.WriteLine($"[{time}] ✓ Reloaded {Path.GetFileName(filePath)}");
            }
            else
            {
                Console.Error.WriteLine($"  ✗ Error: {response.Error}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  ✗ {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts the inner content XAML from a file.
    /// For a Page/UserControl/Window, returns the content inside the root element.
    /// For other elements (Grid, StackPanel, etc.), returns the whole XAML.
    /// </summary>
    private static string ExtractInnerContent(string xaml)
    {
        // Strip XML declaration if present
        var content = xaml.TrimStart();
        if (content.StartsWith("<?xml"))
        {
            var endDecl = content.IndexOf("?>");
            if (endDecl >= 0) content = content[(endDecl + 2)..].TrimStart();
        }

        // Check if root is a Page, UserControl, or Window — these wrap content
        // If so, we want just the inner content element
        var wrapperRoots = new[] { "Page", "UserControl", "Window" };
        foreach (var root in wrapperRoots)
        {
            // Match <Page, <local:Page, <controls:UserControl, etc.
            if (content.StartsWith($"<{root}") || content.Contains($":{root}"))
            {
                // Find the end of the opening tag
                var closeOfOpen = FindEndOfOpeningTag(content);
                if (closeOfOpen > 0)
                {
                    // Find the closing tag
                    var closingTag = content.LastIndexOf($"</{root}", StringComparison.Ordinal);
                    if (closingTag < 0)
                    {
                        // Try with namespace prefix
                        closingTag = content.LastIndexOf("</", StringComparison.Ordinal);
                    }
                    if (closingTag > closeOfOpen)
                    {
                        var inner = content[closeOfOpen..closingTag].Trim();
                        if (!string.IsNullOrEmpty(inner)) return inner;
                    }
                }
            }
        }

        // Not a wrapper root — return the whole XAML
        return content;
    }

    private static int FindEndOfOpeningTag(string xml)
    {
        // Find the end of the first XML tag, handling self-closing and attributes
        int depth = 0;
        bool inQuote = false;
        char quoteChar = '"';

        for (int i = 0; i < xml.Length; i++)
        {
            if (inQuote)
            {
                if (xml[i] == quoteChar) inQuote = false;
                continue;
            }

            if (xml[i] == '"' || xml[i] == '\'')
            {
                inQuote = true;
                quoteChar = xml[i];
                continue;
            }

            if (xml[i] == '<') depth++;
            if (xml[i] == '>')
            {
                depth--;
                if (depth == 0) return i + 1;
            }
        }
        return -1;
    }

    /// <summary>
    /// Auto-detects the target element by parsing the XAML root type and searching the inspect output.
    /// </summary>
    private static async Task<string?> AutoDetectElement(ParseResult parseResult, string filePath)
    {
        try
        {
            var xaml = await File.ReadAllTextAsync(filePath);
            var content = xaml.TrimStart();

            // Strip XML declaration
            if (content.StartsWith("<?xml"))
            {
                var endDecl = content.IndexOf("?>");
                if (endDecl >= 0) content = content[(endDecl + 2)..].TrimStart();
            }

            // Extract root element type (e.g., "Page", "Grid", "StackPanel")
            if (content.StartsWith('<'))
            {
                var endOfTag = content.IndexOfAny([' ', '>', '/', '\r', '\n'], 1);
                if (endOfTag > 1)
                {
                    var rootType = content[1..endOfTag];
                    // Strip namespace prefix (e.g., "local:MyControl" → "MyControl")
                    var colonIdx = rootType.IndexOf(':');
                    if (colonIdx >= 0) rootType = rootType[(colonIdx + 1)..];

                    Console.Error.WriteLine($"XAML root type: {rootType}");

                    // Inspect the live tree and search the JSON locally
                    using var client = await CommandHelpers.GetConnectedClient(parseResult);
                    var response = await client.SendCommandAsync(Protocol.Commands.Inspect, null);

                    if (response.Success && response.Data.HasValue)
                    {
                        // DFS search in the JSON tree for matching type
                        var id = FindElementByType(response.Data.Value, rootType);
                        return id;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// DFS search in inspect JSON output for the first element matching the given type.
    /// Prefers named elements (with x:Name) over unnamed ones. Skips the root element.
    /// </summary>
    private static string? FindElementByType(JsonElement node, string type, bool isRoot = true)
    {
        string? firstUnnamed = null;

        FindElementByTypeRecursive(node, type, isRoot, ref firstUnnamed);

        return firstUnnamed; // Will be set to the first named match, or first unnamed if no named found
    }

    private static bool FindElementByTypeRecursive(JsonElement node, string type, bool isRoot, ref string? bestMatch)
    {
        if (!isRoot && node.TryGetProperty("type", out var typeProp))
        {
            var nodeType = typeProp.GetString();
            if (string.Equals(nodeType, type, StringComparison.OrdinalIgnoreCase))
            {
                if (node.TryGetProperty("id", out var idProp))
                {
                    var id = idProp.GetString();
                    bool hasName = node.TryGetProperty("name", out var nameProp) && nameProp.GetString() != null;

                    if (hasName)
                    {
                        // Named element — prefer this immediately
                        bestMatch = id;
                        return true; // Stop searching
                    }
                    else if (bestMatch == null)
                    {
                        // First unnamed match — save as fallback
                        bestMatch = id;
                    }
                }
            }
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                if (FindElementByTypeRecursive(child, type, false, ref bestMatch))
                    return true; // Found named match, stop
            }
        }

        return false;
    }

    /// <summary>
    /// Finds an element by x:Name by running inspect and searching the JSON tree.
    /// </summary>
    private static async Task<string?> FindElementByName(ParseResult parseResult, string targetName)
    {
        try
        {
            using var client = await CommandHelpers.GetConnectedClient(parseResult);
            var response = await client.SendCommandAsync(Protocol.Commands.Inspect, null);

            if (response.Success && response.Data.HasValue)
            {
                return FindNodeByName(response.Data.Value, targetName);
            }
        }
        catch { }
        return null;
    }

    private static string? FindNodeByName(JsonElement node, string targetName)
    {
        if (node.TryGetProperty("name", out var nameProp))
        {
            if (string.Equals(nameProp.GetString(), targetName, StringComparison.OrdinalIgnoreCase))
            {
                if (node.TryGetProperty("id", out var idProp))
                    return idProp.GetString();
            }
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                var found = FindNodeByName(child, targetName);
                if (found != null) return found;
            }
        }

        return null;
    }
}
