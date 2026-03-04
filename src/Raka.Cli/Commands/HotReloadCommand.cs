using System.CommandLine;
using System.Text.Json;
using Raka.Protocol;

namespace Raka.Cli.Commands;

/// <summary>
/// Watches XAML files and hot-reloads the running app on each save.
/// Supports single-file mode and directory-wide watch mode.
/// Uses XamlReader.Load() via the NuGet pipe to parse and replace content.
/// </summary>
internal static class HotReloadCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string?>("file") { Description = "XAML file or directory to watch. If a directory, watches all *.xaml files.", Arity = ArgumentArity.ZeroOrOne };
        var elementOption = new Option<string?>("--element", "-e") { Description = "Element ID to replace (single-file mode only)" };
        var nameOption = new Option<string?>("--target-name", "-n") { Description = "Target element by x:Name (single-file mode only)" };

        var command = new Command("hot-reload", "Watch XAML file(s) and hot-reload the running app on save")
        {
            fileArg,
            elementOption,
            nameOption,
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult, ct) =>
        {
            var fileOrDir = parseResult.GetValue(fileArg);
            var elementId = parseResult.GetValue(elementOption);
            var targetName = parseResult.GetValue(nameOption);

            // Default to current directory if nothing specified
            fileOrDir ??= Directory.GetCurrentDirectory();

            var fullPath = Path.GetFullPath(fileOrDir);

            if (Directory.Exists(fullPath))
            {
                // Directory mode — watch all XAML files
                await RunDirectoryMode(parseResult, fullPath);
            }
            else if (File.Exists(fullPath))
            {
                // Single-file mode — existing behavior
                await RunSingleFileMode(parseResult, fullPath, elementId, targetName);
            }
            else
            {
                Console.Error.WriteLine($"Not found: {fullPath}");
                Environment.ExitCode = 1;
            }
        });

        return command;
    }

    /// <summary>
    /// Watches a single XAML file and reloads a specific element.
    /// </summary>
    private static async Task RunSingleFileMode(ParseResult parseResult, string fullPath, string? elementId, string? targetName)
    {
        Console.Error.WriteLine($"Watching: {fullPath}");

        // Resolve target element
        if (elementId == null && targetName != null)
        {
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

        await ReloadXaml(parseResult, fullPath, elementId);

        var dir = Path.GetDirectoryName(fullPath)!;
        var fileName = Path.GetFileName(fullPath);
        using var watcher = new FileSystemWatcher(dir, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        var debounce = DateTime.MinValue;
        var debounceLock = new object();
        var tcs = new TaskCompletionSource();

        watcher.Changed += async (_, e) =>
        {
            var now = DateTime.UtcNow;
            lock (debounceLock)
            {
                if ((now - debounce).TotalMilliseconds < 300) return;
                debounce = now;
            }
            await Task.Delay(100);
            await ReloadXaml(parseResult, fullPath, elementId);
        };

        Console.Error.WriteLine("Hot reload active. Save the file to reload. Press Ctrl+C to stop.");
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.TrySetResult(); };
        await tcs.Task;
        Console.Error.WriteLine("Hot reload stopped.");
    }

    /// <summary>
    /// Watches all XAML files in a directory and auto-maps each to its element via x:Class.
    /// </summary>
    private static async Task RunDirectoryMode(ParseResult parseResult, string directory)
    {
        // Find all XAML files and parse x:Class to build the mapping
        var xamlFiles = Directory.GetFiles(directory, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\") && !f.EndsWith(".g.xaml"))
            .ToList();

        if (xamlFiles.Count == 0)
        {
            Console.Error.WriteLine($"No XAML files found in {directory}");
            Environment.ExitCode = 1;
            return;
        }

        Console.Error.WriteLine($"Watching {xamlFiles.Count} XAML file(s) in {directory}");

        // Build the x:Class → file path map and discover which files are relevant
        var classMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in xamlFiles)
        {
            var className = ParseXClass(file);
            if (className != null)
            {
                classMap[className] = file;
                var shortName = Path.GetRelativePath(directory, file);
                Console.Error.WriteLine($"  {shortName} → {className}");
            }
        }

        if (classMap.Count == 0)
        {
            Console.Error.WriteLine("No XAML files with x:Class found.");
            Environment.ExitCode = 1;
            return;
        }

        // Do initial inspection to understand the live tree
        Console.Error.WriteLine("Inspecting live tree...");
        JsonElement? treeSnapshot = null;
        try
        {
            using var client = await CommandHelpers.GetConnectedClient(parseResult);
            var response = await client.SendCommandAsync(Protocol.Commands.Inspect, null);
            if (response.Success && response.Data.HasValue)
                treeSnapshot = response.Data.Value.Clone();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  ✗ Cannot connect: {ex.Message}");
            Environment.ExitCode = 1;
            return;
        }

        // Map each class to its target element ID
        // The root of the tree corresponds to the Window's content
        // Other classes (Pages, UserControls) need to be found by className
        var fileToElementId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (className, filePath) in classMap)
        {
            var shortClass = className.Contains('.') ? className[(className.LastIndexOf('.') + 1)..] : className;

            if (treeSnapshot.HasValue)
            {
                // For the main window, the tree root IS its content
                // Check if this class is a Window type by reading the XAML
                var rootType = ParseRootType(filePath);

                if (rootType is "Window")
                {
                    // The Window's content is the root of the inspect tree (e0)
                    if (treeSnapshot.Value.TryGetProperty("id", out var rootId))
                    {
                        fileToElementId[filePath] = rootId.GetString()!;
                        Console.Error.WriteLine($"  {shortClass} → {rootId.GetString()} (window content)");
                    }
                }
                else
                {
                    // Search for this type by className in the tree
                    var id = FindElementByClassName(treeSnapshot.Value, className)
                          ?? FindElementByType(treeSnapshot.Value, shortClass);

                    // For Page types not found in the visual tree, check Frame.Content
                    if (id == null && rootType is "Page")
                    {
                        id = FindPageInFrame(treeSnapshot.Value, className);
                        if (id != null)
                        {
                            fileToElementId[filePath] = id;
                            Console.Error.WriteLine($"  {shortClass} → {id} (via Frame.Content)");
                        }
                    }

                    if (id != null && !fileToElementId.ContainsKey(filePath))
                    {
                        fileToElementId[filePath] = id;
                        Console.Error.WriteLine($"  {shortClass} → {id}");
                    }
                    else if (id == null)
                    {
                        Console.Error.WriteLine($"  {shortClass} → (not found in live tree — may not be visible)");
                    }
                }
            }
        }

        if (fileToElementId.Count == 0)
        {
            Console.Error.WriteLine("No XAML files could be mapped to live elements.");
            Environment.ExitCode = 1;
            return;
        }

        Console.Error.WriteLine($"Mapped {fileToElementId.Count} file(s) to live elements.");

        // Seed the reconciler cache with current file contents so subsequent
        // edits can use in-place reconciliation (even for files with event handlers
        // that XamlReader.Load can't parse for full replacement).
        Console.Error.WriteLine("Seeding reconciler cache...");
        foreach (var (filePath, elementId) in fileToElementId)
        {
            await ReloadXaml(parseResult, filePath, elementId);
        }

        // Watch directory for changes
        using var watcher = new FileSystemWatcher(directory, "*.xaml")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        var debounceMap = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var debounceLock = new object();
        var tcs = new TaskCompletionSource();

        watcher.Changed += async (_, e) =>
        {
            var changedPath = Path.GetFullPath(e.FullPath);

            // Thread-safe debounce per-file
            var now = DateTime.UtcNow;
            lock (debounceLock)
            {
                if (debounceMap.TryGetValue(changedPath, out var last) && (now - last).TotalMilliseconds < 300)
                    return;
                debounceMap[changedPath] = now;
            }

            await Task.Delay(100);

            if (fileToElementId.TryGetValue(changedPath, out var elementId))
            {
                await ReloadXaml(parseResult, changedPath, elementId);
            }
            else
            {
                // File not mapped — try to map it dynamically
                var className = ParseXClass(changedPath);
                if (className != null)
                {
                    var rootType = ParseRootType(changedPath);
                    string? id = null;

                    // Re-inspect to find the element
                    try
                    {
                        using var client = await CommandHelpers.GetConnectedClient(parseResult);
                        var response = await client.SendCommandAsync(Protocol.Commands.Inspect, null);
                        if (response.Success && response.Data.HasValue)
                        {
                            if (rootType is "Window")
                            {
                                if (response.Data.Value.TryGetProperty("id", out var rootId))
                                    id = rootId.GetString();
                            }
                            else
                            {
                                var shortClass = className.Contains('.') ? className[(className.LastIndexOf('.') + 1)..] : className;
                                id = FindElementByClassName(response.Data.Value, className)
                                  ?? FindElementByType(response.Data.Value, shortClass);

                                // For Page types, check Frame.Content
                                if (id == null && rootType is "Page")
                                    id = FindPageInFrame(response.Data.Value, className);
                            }
                        }
                    }
                    catch { }

                    if (id != null)
                    {
                        fileToElementId[changedPath] = id;
                        await ReloadXaml(parseResult, changedPath, id);
                    }
                    else
                    {
                        var shortName = Path.GetRelativePath(directory, changedPath);
                        Console.Error.WriteLine($"  ⊘ {shortName} — not mapped to a live element (not visible?)");
                    }
                }
            }
        };

        Console.Error.WriteLine("Hot reload active. Save any XAML file to reload. Press Ctrl+C to stop.");
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.TrySetResult(); };
        await tcs.Task;
        Console.Error.WriteLine("Hot reload stopped.");
    }

    /// <summary>
    /// Parses x:Class from a XAML file. Returns the full class name or null.
    /// </summary>
    private static string? ParseXClass(string filePath)
    {
        try
        {
            // Read just enough to find x:Class (usually in first few lines)
            using var reader = new StreamReader(filePath);
            var buffer = new char[2048];
            var read = reader.Read(buffer, 0, buffer.Length);
            var text = new string(buffer, 0, read);

            var marker = "x:Class=\"";
            var idx = text.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return null;

            var start = idx + marker.Length;
            var end = text.IndexOf('"', start);
            if (end < 0) return null;

            return text[start..end];
        }
        catch { return null; }
    }

    /// <summary>
    /// Parses the root element type from a XAML file (e.g., "Window", "Page", "UserControl").
    /// </summary>
    private static string? ParseRootType(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            var buffer = new char[2048];
            var read = reader.Read(buffer, 0, buffer.Length);
            var text = new string(buffer, 0, read);

            // Skip XML declaration
            if (text.StartsWith("<?xml"))
            {
                var endDecl = text.IndexOf("?>");
                if (endDecl >= 0) text = text[(endDecl + 2)..].TrimStart();
            }

            if (text.StartsWith('<'))
            {
                var endOfTag = text.IndexOfAny([' ', '>', '/', '\r', '\n'], 1);
                if (endOfTag > 1)
                {
                    var rootType = text[1..endOfTag];
                    var colonIdx = rootType.IndexOf(':');
                    if (colonIdx >= 0) rootType = rootType[(colonIdx + 1)..];
                    return rootType;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Reloads the XAML file content into the running app.
    /// </summary>
    private static async Task ReloadXaml(ParseResult parseResult, string filePath, string elementId)
    {
        try
        {
            var xaml = await File.ReadAllTextAsync(filePath);
            var innerXaml = ExtractInnerContent(xaml);

            using var client = await CommandHelpers.GetConnectedClient(parseResult);

            // Ensure the tree walker is populated — replace needs elements indexed.
            await client.SendCommandAsync(Protocol.Commands.Inspect, null);

            var p = new ReplaceXamlParams(elementId, innerXaml);
            var parameters = JsonSerializer.SerializeToElement(p, CliJsonContext.Default.ReplaceXamlParams);
            var response = await client.SendCommandAsync(Protocol.Commands.ReplaceXaml, parameters);

            if (response.Success)
            {
                var time = DateTime.Now.ToString("HH:mm:ss");
                var shortName = Path.GetFileName(filePath);
                var mode = "replace";
                var extra = "";
                if (response.Data.HasValue)
                {
                    if (response.Data.Value.TryGetProperty("mode", out var modeProp))
                        mode = modeProp.GetString() ?? "replace";
                    if (response.Data.Value.TryGetProperty("patches", out var patchesProp))
                        extra = $", {patchesProp.GetInt32()} patches";
                    if (response.Data.Value.TryGetProperty("reconcileError", out var errProp) && errProp.GetString() is string err)
                        extra += $", reconcile failed: {err}";
                }
                var count = response.Data.HasValue ? CountElements(response.Data.Value) : 0;
                Console.Error.WriteLine($"[{time}] ✓ Reloaded {shortName} (mode={mode}{extra})");
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
    /// Counts elements in a JSON tree response (id + children recursively).
    /// </summary>
    private static int CountElements(JsonElement node)
    {
        int count = 1;
        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                count += CountElements(child);
        }
        return count;
    }

    /// <summary>
    /// Extracts the inner content XAML from a file.
    /// For a Page/UserControl/Window, returns the content child element (skipping attached properties).
    /// Namespace declarations from the wrapper are carried over to the inner content.
    /// For other elements (Grid, StackPanel, etc.), returns the whole XAML.
    /// </summary>
    private static string ExtractInnerContent(string xaml)
    {
        var content = xaml.TrimStart();
        if (content.StartsWith("<?xml"))
        {
            var endDecl = content.IndexOf("?>");
            if (endDecl >= 0) content = content[(endDecl + 2)..].TrimStart();
        }

        var wrapperRoots = new[] { "Page", "UserControl", "Window" };
        foreach (var root in wrapperRoots)
        {
            if (content.StartsWith($"<{root}") || content.Contains($":{root}"))
            {
                var closeOfOpen = FindEndOfOpeningTag(content);
                if (closeOfOpen > 0)
                {
                    // Extract xmlns declarations from the wrapper's opening tag
                    var openingTag = content[..closeOfOpen];
                    var namespaces = ExtractNamespaceDeclarations(openingTag);

                    var closingTag = content.LastIndexOf($"</{root}", StringComparison.Ordinal);
                    if (closingTag < 0)
                        closingTag = content.LastIndexOf("</", StringComparison.Ordinal);
                    if (closingTag > closeOfOpen)
                    {
                        var inner = content[closeOfOpen..closingTag].Trim();
                        if (!string.IsNullOrEmpty(inner))
                        {
                            inner = SkipAttachedProperties(inner, root);
                            if (!string.IsNullOrEmpty(inner))
                            {
                                // Inject parent namespaces into the inner content's root element
                                if (namespaces.Count > 0)
                                    inner = InjectNamespaces(inner, namespaces);
                                return inner;
                            }
                        }
                    }
                }
            }
        }

        return content;
    }

    /// <summary>
    /// Extracts xmlns:xxx="..." declarations from an XML opening tag.
    /// Returns pairs to avoid duplicates when injecting.
    /// </summary>
    private static List<string> ExtractNamespaceDeclarations(string openingTag)
    {
        var result = new List<string>();
        var idx = 0;
        while (true)
        {
            idx = openingTag.IndexOf("xmlns:", idx, StringComparison.Ordinal);
            if (idx < 0) break;

            // Find the end of this declaration: xmlns:local="using:SampleApp"
            var eqIdx = openingTag.IndexOf('=', idx);
            if (eqIdx < 0) break;

            var quoteChar = openingTag[eqIdx + 1];
            var endQuote = openingTag.IndexOf(quoteChar, eqIdx + 2);
            if (endQuote < 0) break;

            var decl = openingTag[idx..(endQuote + 1)];

            // Skip standard x: and d: and mc: namespaces — they're for the compiler only
            if (!decl.StartsWith("xmlns:x=") && !decl.StartsWith("xmlns:d=") && !decl.StartsWith("xmlns:mc="))
            {
                result.Add(decl);
            }

            idx = endQuote + 1;
        }
        return result;
    }

    /// <summary>
    /// Injects namespace declarations into the first element of the inner XAML.
    /// </summary>
    private static string InjectNamespaces(string xaml, List<string> namespaces)
    {
        // Find the first '>' or '/>' in the root element to inject before it
        // But we need to be careful about quotes
        var firstGt = -1;
        bool inQuote = false;
        char quoteChar = '"';

        for (int i = 0; i < xaml.Length; i++)
        {
            if (inQuote) { if (xaml[i] == quoteChar) inQuote = false; continue; }
            if (xaml[i] == '"' || xaml[i] == '\'') { inQuote = true; quoteChar = xaml[i]; continue; }
            if (xaml[i] == '>')
            {
                firstGt = i;
                break;
            }
        }

        if (firstGt < 0) return xaml;

        // Check for self-closing (/>)
        var insertPos = (firstGt > 0 && xaml[firstGt - 1] == '/') ? firstGt - 1 : firstGt;

        // Build namespace string, only inject ones not already present
        var toInject = new List<string>();
        foreach (var ns in namespaces)
        {
            if (!xaml.Contains(ns.Split('=')[0] + "="))
                toInject.Add(ns);
        }

        if (toInject.Count == 0) return xaml;

        var nsString = " " + string.Join(" ", toInject);
        return xaml[..insertPos] + nsString + xaml[insertPos..];
    }

    /// <summary>
    /// Skips attached property elements (e.g., Window.SystemBackdrop, Page.Resources)
    /// and returns only the actual content child element.
    /// </summary>
    private static string SkipAttachedProperties(string inner, string parentType)
    {
        var remaining = inner;
        while (remaining.Length > 0)
        {
            remaining = remaining.TrimStart();
            if (remaining.Length == 0) break;

            if (!remaining.StartsWith('<')) break;

            // Check if this is an attached property element: <Window.Xxx> or <Page.Xxx>
            bool isAttachedProp = false;
            foreach (var prefix in new[] { $"<{parentType}.", "<Window.", "<Page.", "<UserControl." })
            {
                if (remaining.StartsWith(prefix, StringComparison.Ordinal))
                {
                    isAttachedProp = true;
                    break;
                }
            }

            if (!isAttachedProp)
            {
                // This is the content element — return everything from here
                return remaining;
            }

            // Skip this attached property element entirely
            var endOfBlock = FindMatchingCloseTag(remaining);
            if (endOfBlock < 0) break;
            remaining = remaining[endOfBlock..];
        }

        return remaining;
    }

    /// <summary>
    /// Finds the end of the first XML element (including children) in the string.
    /// Returns the index after the closing tag.
    /// </summary>
    private static int FindMatchingCloseTag(string xml)
    {
        int depth = 0;
        bool inQuote = false;
        char quoteChar = '"';

        for (int i = 0; i < xml.Length; i++)
        {
            if (inQuote) { if (xml[i] == quoteChar) inQuote = false; continue; }
            if (xml[i] == '"' || xml[i] == '\'') { inQuote = true; quoteChar = xml[i]; continue; }

            if (xml[i] == '<')
            {
                // Check for self-closing or closing tag
                if (i + 1 < xml.Length && xml[i + 1] == '/')
                {
                    depth--;
                    // Find the end of this closing tag
                    var closeEnd = xml.IndexOf('>', i);
                    if (closeEnd >= 0 && depth == 0) return closeEnd + 1;
                    if (closeEnd >= 0) i = closeEnd;
                }
                else
                {
                    depth++;
                }
            }
            else if (xml[i] == '/')
            {
                // Self-closing: />
                if (i + 1 < xml.Length && xml[i + 1] == '>')
                {
                    depth--;
                    if (depth == 0) return i + 2;
                    i++; // skip the >
                }
            }
            else if (xml[i] == '>')
            {
                // Just the end of an opening tag — depth already incremented
            }
        }
        return -1;
    }

    private static int FindEndOfOpeningTag(string xml)
    {
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
            if (xml[i] == '"' || xml[i] == '\'') { inQuote = true; quoteChar = xml[i]; continue; }
            if (xml[i] == '<') depth++;
            if (xml[i] == '>') { depth--; if (depth == 0) return i + 1; }
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

            if (content.StartsWith("<?xml"))
            {
                var endDecl = content.IndexOf("?>");
                if (endDecl >= 0) content = content[(endDecl + 2)..].TrimStart();
            }

            if (content.StartsWith('<'))
            {
                var endOfTag = content.IndexOfAny([' ', '>', '/', '\r', '\n'], 1);
                if (endOfTag > 1)
                {
                    var rootType = content[1..endOfTag];
                    var colonIdx = rootType.IndexOf(':');
                    if (colonIdx >= 0) rootType = rootType[(colonIdx + 1)..];

                    Console.Error.WriteLine($"XAML root type: {rootType}");

                    using var client = await CommandHelpers.GetConnectedClient(parseResult);
                    var response = await client.SendCommandAsync(Protocol.Commands.Inspect, null);

                    if (response.Success && response.Data.HasValue)
                    {
                        // For Page/Window/UserControl, use x:Class to find via Frame
                        if (rootType is "Page" or "Window" or "UserControl")
                        {
                            var xClass = ExtractXClass(xaml);
                            if (xClass != null)
                            {
                                var found = FindPageInFrame(response.Data.Value, xClass);
                                if (found != null) return found;
                                // Also try direct className match
                                found = FindElementByClassName(response.Data.Value, xClass);
                                if (found != null) return found;
                            }
                        }

                        return FindElementByType(response.Data.Value, rootType);
                    }
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Extracts the x:Class value from XAML content (e.g., "MyApp.Pages.SettingsPage").
    /// </summary>
    private static string? ExtractXClass(string xaml)
    {
        const string marker = "x:Class=\"";
        var idx = xaml.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var start = idx + marker.Length;
        var end = xaml.IndexOf('"', start);
        return end > start ? xaml[start..end] : null;
    }

    /// <summary>
    /// DFS search for the first element matching a type name.
    /// Prefers named elements (with x:Name) over unnamed ones.
    /// </summary>
    private static string? FindElementByType(JsonElement node, string type, bool isRoot = true)
    {
        string? bestMatch = null;
        FindElementByTypeRecursive(node, type, isRoot, ref bestMatch);
        return bestMatch;
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
                    if (hasName) { bestMatch = id; return true; }
                    else if (bestMatch == null) { bestMatch = id; }
                }
            }
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                if (FindElementByTypeRecursive(child, type, false, ref bestMatch))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// DFS search for an element with a matching className (full namespace).
    /// </summary>
    private static string? FindElementByClassName(JsonElement node, string className)
    {
        if (node.TryGetProperty("className", out var cnProp))
        {
            if (string.Equals(cnProp.GetString(), className, StringComparison.OrdinalIgnoreCase))
            {
                if (node.TryGetProperty("id", out var idProp))
                    return idProp.GetString();
            }
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                var found = FindElementByClassName(child, className);
                if (found != null) return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds a Page loaded in a Frame by checking Frame.contentClassName.
    /// Returns the ID of the Frame's ContentPresenter's first child (the actual Page content root).
    /// Pages don't appear in the VisualTreeHelper walk — the Frame renders them through a ContentPresenter.
    /// </summary>
    private static string? FindPageInFrame(JsonElement node, string pageClassName)
    {
        // Check if this is a Frame with matching contentClassName
        if (node.TryGetProperty("contentClassName", out var ccn) &&
            string.Equals(ccn.GetString(), pageClassName, StringComparison.OrdinalIgnoreCase))
        {
            // Found the Frame — navigate to ContentPresenter → first child
            if (node.TryGetProperty("children", out var frameChildren) && frameChildren.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in frameChildren.EnumerateArray())
                {
                    // Look for ContentPresenter (first child of Frame)
                    if (child.TryGetProperty("type", out var typeProp) &&
                        typeProp.GetString() == "ContentPresenter")
                    {
                        // The ContentPresenter's first child is the Page's content root
                        if (child.TryGetProperty("children", out var cpChildren) && cpChildren.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var grandChild in cpChildren.EnumerateArray())
                            {
                                if (grandChild.TryGetProperty("id", out var idProp))
                                    return idProp.GetString();
                            }
                        }
                    }
                }
            }
            // Fallback: return the Frame's ID itself
            if (node.TryGetProperty("id", out var frameId))
                return frameId.GetString();
        }

        // Recurse into children
        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                var found = FindPageInFrame(child, pageClassName);
                if (found != null) return found;
            }
        }
        return null;
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
                return FindNodeByName(response.Data.Value, targetName);
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
