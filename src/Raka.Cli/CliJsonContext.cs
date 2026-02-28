using System.Text.Json;
using System.Text.Json.Serialization;
using Raka.Cli.Session;
using Raka.Protocol;

namespace Raka.Cli;

/// <summary>
/// Source-generated JSON context for AOT-compatible serialization.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RakaRequest))]
[JsonSerializable(typeof(RakaResponse))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(SessionManager.SessionInfo))]
[JsonSerializable(typeof(InspectParams))]
[JsonSerializable(typeof(SearchParams))]
[JsonSerializable(typeof(GetPropertyParams))]
[JsonSerializable(typeof(SetPropertyParams))]
[JsonSerializable(typeof(ElementParams))]
[JsonSerializable(typeof(ScreenshotParams))]
internal partial class CliJsonContext : JsonSerializerContext
{
    private static CliJsonContext? _pretty;

    /// <summary>
    /// Context configured with indented output for display.
    /// </summary>
    public static CliJsonContext Pretty => _pretty ??= new CliJsonContext(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        TypeInfoResolver = Default
    });
}

// Typed parameter records for each command — fully AOT-safe

internal record InspectParams(string? Element = null, int? Depth = null);

internal record SearchParams(string? Type = null, string? Name = null, string? Text = null, string? AutomationId = null);

internal record GetPropertyParams(string Element, string? Property = null, bool? All = null);

internal record SetPropertyParams(string Element, string Property, string Value);

/// <summary>Shared param type for commands that only need an element ID (click, ancestors).</summary>
internal record ElementParams(string Element);

internal record ScreenshotParams(string? Element = null, string? Mode = null, string? Background = null);
