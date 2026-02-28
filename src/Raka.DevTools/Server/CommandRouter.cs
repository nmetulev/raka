using System.Text.Json;
using Microsoft.UI.Xaml;
using Raka.DevTools.Core;
using Raka.Protocol;

namespace Raka.DevTools.Server;

/// <summary>
/// Routes incoming CLI commands to the appropriate handler.
/// All methods are called on the UI thread.
/// </summary>
internal sealed class CommandRouter
{
    private readonly VisualTreeWalker _walker = new();
    private Window? _window;

    public void SetWindow(Window window)
    {
        _window = window;
    }

    public RakaResponse Handle(RakaRequest request)
    {
        try
        {
            return request.Command switch
            {
                Commands.Ping => HandlePing(),
                Commands.Inspect => HandleInspect(request.Params),
                Commands.Search => HandleSearch(request.Params),
                Commands.GetProperty => HandleGetProperty(request.Params),
                Commands.SetProperty => HandleSetProperty(request.Params),
                Commands.Ancestors => HandleAncestors(request.Params),
                _ => new RakaResponse { Success = false, Error = $"Unknown command: {request.Command}" }
            };
        }
        catch (Exception ex)
        {
            return new RakaResponse { Success = false, Error = ex.Message };
        }
    }

    private RakaResponse HandlePing()
    {
        var info = new
        {
            app = _window?.Title ?? "Unknown",
            pid = Environment.ProcessId,
            framework = "WinUI3",
            runtimeVersion = Environment.Version.ToString()
        };

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(info, RakaJson.Options)
        };
    }

    private RakaResponse HandleInspect(JsonElement? parameters)
    {
        var root = GetRoot();
        if (root == null)
            return new RakaResponse { Success = false, Error = "No window content available" };

        string? elementId = null;
        int depth = int.MaxValue;

        if (parameters.HasValue)
        {
            if (parameters.Value.TryGetProperty("element", out var elemProp))
                elementId = elemProp.GetString();
            if (parameters.Value.TryGetProperty("depth", out var depthProp))
                depth = depthProp.GetInt32();
        }

        DependencyObject target;
        if (elementId != null)
        {
            target = _walker.GetElement(elementId)
                ?? throw new ArgumentException($"Element '{elementId}' not found. Run 'inspect' first to populate element IDs.");
        }
        else
        {
            target = root;
        }

        var tree = elementId != null
            ? _walker.WalkFrom(target, depth)
            : _walker.Walk(target, depth);

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(tree, RakaJson.Options)
        };
    }

    private RakaResponse HandleSearch(JsonElement? parameters)
    {
        var root = GetRoot();
        if (root == null)
            return new RakaResponse { Success = false, Error = "No window content available" };

        string? type = null, name = null, text = null, automationId = null;

        if (parameters.HasValue)
        {
            if (parameters.Value.TryGetProperty("type", out var typeProp))
                type = typeProp.GetString();
            if (parameters.Value.TryGetProperty("name", out var nameProp))
                name = nameProp.GetString();
            if (parameters.Value.TryGetProperty("text", out var textProp))
                text = textProp.GetString();
            if (parameters.Value.TryGetProperty("automationId", out var autoProp))
                automationId = autoProp.GetString();
        }

        if (type == null && name == null && text == null && automationId == null)
            return new RakaResponse { Success = false, Error = "Specify at least one search criterion: type, name, text, or automationId" };

        var results = _walker.Search(root, type, name, text, automationId);

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(results, RakaJson.Options)
        };
    }

    private RakaResponse HandleGetProperty(JsonElement? parameters)
    {
        if (!parameters.HasValue || !parameters.Value.TryGetProperty("element", out var elemProp))
            return new RakaResponse { Success = false, Error = "Missing 'element' parameter" };

        var elementId = elemProp.GetString()!;
        var element = _walker.GetElement(elementId)
            ?? throw new ArgumentException($"Element '{elementId}' not found. Run 'inspect' first.");

        bool all = false;
        string? propertyName = null;

        if (parameters.Value.TryGetProperty("all", out var allProp))
            all = allProp.GetBoolean();
        if (parameters.Value.TryGetProperty("property", out var propProp))
            propertyName = propProp.GetString();

        if (all)
        {
            var props = PropertyReader.ReadAllProperties(element);
            return new RakaResponse
            {
                Success = true,
                Data = JsonSerializer.SerializeToElement(props, RakaJson.Options)
            };
        }

        if (propertyName == null)
            return new RakaResponse { Success = false, Error = "Specify 'property' name or use 'all': true" };

        var result = PropertyReader.ReadProperty(element, propertyName);
        if (result == null)
            return new RakaResponse { Success = false, Error = $"Property '{propertyName}' not found on {element.GetType().Name}" };

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(result, RakaJson.Options)
        };
    }

    private RakaResponse HandleSetProperty(JsonElement? parameters)
    {
        if (!parameters.HasValue)
            return new RakaResponse { Success = false, Error = "Missing parameters" };

        if (!parameters.Value.TryGetProperty("element", out var elemProp))
            return new RakaResponse { Success = false, Error = "Missing 'element' parameter" };
        if (!parameters.Value.TryGetProperty("property", out var propProp))
            return new RakaResponse { Success = false, Error = "Missing 'property' parameter" };
        if (!parameters.Value.TryGetProperty("value", out var valProp))
            return new RakaResponse { Success = false, Error = "Missing 'value' parameter" };

        var elementId = elemProp.GetString()!;
        var propertyName = propProp.GetString()!;
        var valueStr = valProp.GetString()!;

        var element = _walker.GetElement(elementId)
            ?? throw new ArgumentException($"Element '{elementId}' not found. Run 'inspect' first.");

        PropertyWriter.SetProperty(element, propertyName, valueStr);

        // Read back to confirm
        var updated = PropertyReader.ReadProperty(element, propertyName);
        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(updated, RakaJson.Options)
        };
    }

    private RakaResponse HandleAncestors(JsonElement? parameters)
    {
        if (!parameters.HasValue || !parameters.Value.TryGetProperty("element", out var elemProp))
            return new RakaResponse { Success = false, Error = "Missing 'element' parameter" };

        var elementId = elemProp.GetString()!;
        var element = _walker.GetElement(elementId)
            ?? throw new ArgumentException($"Element '{elementId}' not found. Run 'inspect' first.");

        var ancestors = _walker.GetAncestors(element);
        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(ancestors, RakaJson.Options)
        };
    }

    private DependencyObject? GetRoot()
    {
        return _window?.Content as DependencyObject;
    }
}
