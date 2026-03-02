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
[JsonSerializable(typeof(AddXamlParams))]
[JsonSerializable(typeof(ReplaceXamlParams))]
[JsonSerializable(typeof(NavigateParams))]
[JsonSerializable(typeof(ClickParams))]
[JsonSerializable(typeof(InvokeParams))]
[JsonSerializable(typeof(TypeParams))]
[JsonSerializable(typeof(HotkeyParams))]
[JsonSerializable(typeof(GetStatesParams))]
[JsonSerializable(typeof(SetStateParams))]
[JsonSerializable(typeof(StylesParams))]
[JsonSerializable(typeof(ResourcesParams))]
[JsonSerializable(typeof(SetResourceParams))]
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

internal record SearchParams(string? Type = null, string? Name = null, string? Text = null, string? AutomationId = null,
    string? ClassName = null, bool? Interactive = null, bool? VisibleOnly = null, string? Property = null);

internal record GetPropertyParams(string Element, string? Property = null, bool? All = null);

internal record SetPropertyParams(string? Element, string Property, string Value, string? Name = null);

/// <summary>Shared param type for commands that only need an element ID (click, ancestors).</summary>
internal record ElementParams(string Element);

internal record ScreenshotParams(string? Element = null, string? Mode = null, string? Background = null, string? State = null);

internal record AddXamlParams(string Parent, string Xaml, int? Index = null);

internal record ReplaceXamlParams(string Element, string Xaml);

internal record NavigateParams(string Page);

internal record ClickParams(string? Element = null, string? Name = null, string? Type = null, string? Text = null);

internal record InvokeParams(string? Element = null, string? Name = null, string? Type = null, string? Text = null);

internal record TypeParams(string Text, string? Element = null, string? Name = null, int? Delay = null);

internal record HotkeyParams(string Keys);

internal record GetStatesParams(string? Element = null, string? Name = null);

internal record SetStateParams(string? Element = null, string? Name = null, string? State = null, string? Group = null);

internal record StylesParams(string? Element = null, string? Name = null);

internal record ResourcesParams(string? Scope = null, string? Filter = null, string? Theme = null, string? Element = null);

internal record SetResourceParams(string Key, string Value, string? Scope = null, bool Apply = false);
