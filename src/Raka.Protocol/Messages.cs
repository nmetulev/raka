using System.Text.Json;
using System.Text.Json.Serialization;

namespace Raka.Protocol;

/// <summary>
/// A request sent from CLI to the DevTools pipe server.
/// </summary>
public sealed class RakaRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

/// <summary>
/// A response sent from the DevTools pipe server back to the CLI.
/// </summary>
public sealed class RakaResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Serializable representation of a XAML element in the visual tree.
/// </summary>
public sealed class ElementNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("automationId")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("className")]
    public string ClassName { get; set; } = "";

    [JsonPropertyName("bounds")]
    public ElementBounds? Bounds { get; set; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }

    [JsonPropertyName("children")]
    public List<ElementNode>? Children { get; set; }

    [JsonPropertyName("childCount")]
    public int ChildCount { get; set; }

    [JsonPropertyName("contentClassName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentClassName { get; set; }

    [JsonPropertyName("sourceFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceFile { get; set; }
}

public sealed class ElementBounds
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

/// <summary>
/// A single property value of an element.
/// </summary>
public sealed class PropertyValue
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

/// <summary>
/// Well-known command names for the pipe protocol.
/// </summary>
public static class Commands
{
    public const string Inspect = "inspect";
    public const string Search = "search";
    public const string GetProperty = "get-property";
    public const string SetProperty = "set-property";
    public const string Screenshot = "screenshot";
    public const string Ancestors = "ancestors";
    public const string Styles = "styles";
    public const string Resources = "resources";
    public const string Ping = "ping";
    public const string Click = "click";
    public const string AddXaml = "add-xaml";
    public const string RemoveElement = "remove";
    public const string ReplaceXaml = "replace";
    public const string Status = "status";
    public const string Navigate = "navigate";
    public const string ListPages = "list-pages";
    public const string Type = "type";
}

/// <summary>
/// Pipe name convention: Raka.DevTools listens on this pipe.
/// </summary>
public static class PipeNames
{
    public static string ForProcess(int processId) => $"raka-devtools-{processId}";
}

/// <summary>
/// Shared JSON serializer options.
/// </summary>
public static class RakaJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static readonly JsonSerializerOptions PrettyOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}
