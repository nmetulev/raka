using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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

    public async Task<RakaResponse> HandleAsync(RakaRequest request)
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
                Commands.Click => HandleClick(request.Params),
                Commands.Screenshot => await HandleScreenshotAsync(request.Params),
                Commands.AddXaml => HandleAddXaml(request.Params),
                Commands.RemoveElement => HandleRemove(request.Params),
                Commands.ReplaceXaml => HandleReplace(request.Params),
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

    private RakaResponse HandleClick(JsonElement? parameters)
    {
        if (!parameters.HasValue || !parameters.Value.TryGetProperty("element", out var elemProp))
            return new RakaResponse { Success = false, Error = "Missing 'element' parameter" };

        var elementId = elemProp.GetString()!;
        var element = _walker.GetElement(elementId)
            ?? throw new ArgumentException($"Element '{elementId}' not found. Run 'inspect' first.");

        if (element is not UIElement uiElement)
            return new RakaResponse { Success = false, Error = $"Element {elementId} ({element.GetType().Name}) is not a UIElement" };

        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(uiElement);
        if (peer == null)
            return new RakaResponse { Success = false, Error = $"No automation peer for {element.GetType().Name}" };

        // Try Invoke (buttons, hyperlinks, menu items)
        if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoker)
        {
            invoker.Invoke();
            return new RakaResponse
            {
                Success = true,
                Data = JsonSerializer.SerializeToElement(new { action = "invoke", element = elementId, type = element.GetType().Name }, RakaJson.Options)
            };
        }

        // Try Toggle (checkboxes, toggle switches, toggle buttons)
        if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggler)
        {
            toggler.Toggle();
            var newState = toggler.ToggleState.ToString();
            return new RakaResponse
            {
                Success = true,
                Data = JsonSerializer.SerializeToElement(new { action = "toggle", element = elementId, type = element.GetType().Name, state = newState }, RakaJson.Options)
            };
        }

        // Try SelectionItem (radio buttons, list items)
        if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selector)
        {
            selector.Select();
            return new RakaResponse
            {
                Success = true,
                Data = JsonSerializer.SerializeToElement(new { action = "select", element = elementId, type = element.GetType().Name }, RakaJson.Options)
            };
        }

        return new RakaResponse { Success = false, Error = $"{element.GetType().Name} does not support click, toggle, or select" };
    }

    private async Task<RakaResponse> HandleScreenshotAsync(JsonElement? parameters)
    {
        string? elementId = null;
        string mode = "auto"; // auto, capture, render
        string? background = null;

        if (parameters.HasValue)
        {
            if (parameters.Value.TryGetProperty("element", out var elemProp))
                elementId = elemProp.GetString();
            if (parameters.Value.TryGetProperty("mode", out var modeProp))
                mode = modeProp.GetString() ?? "auto";
            if (parameters.Value.TryGetProperty("background", out var bgProp))
                background = bgProp.GetString();
        }

        // Auto mode: capture for whole window, render for specific elements
        if (mode == "auto")
            mode = elementId == null ? "capture" : "render";

        byte[] pngBytes;
        int width, height;

        if (mode == "capture")
        {
            if (_window == null)
                throw new InvalidOperationException("No window available");

            if (elementId != null)
            {
                var el = _walker.GetElement(elementId) as UIElement
                    ?? throw new ArgumentException($"Element '{elementId}' is not a UIElement or not found");
                (pngBytes, width, height) = await ScreenCapturer.CaptureElementFromWindowAsync(_window, el);
            }
            else
            {
                (pngBytes, width, height) = await ScreenCapturer.CaptureWindowAsync(_window);
            }
        }
        else // render mode (RenderTargetBitmap)
        {
            UIElement target;
            if (elementId != null)
            {
                var element = _walker.GetElement(elementId)
                    ?? throw new ArgumentException($"Element '{elementId}' not found. Run 'inspect' first.");
                if (element is not UIElement uiEl)
                    return new RakaResponse { Success = false, Error = $"Element {elementId} ({element.GetType().Name}) is not a UIElement" };
                target = uiEl;
            }
            else
            {
                target = _window?.Content as UIElement
                    ?? throw new InvalidOperationException("No window content available");
            }

            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(target);

            var pixelBuffer = await rtb.GetPixelsAsync();
            var pixels = pixelBuffer.ToArray();
            width = rtb.PixelWidth;
            height = rtb.PixelHeight;

            if (background != null)
            {
                var (r, g, b) = ScreenCapturer.ParseHexColor(background);
                ScreenCapturer.ApplyBackground(pixels, r, g, b);
            }

            pngBytes = await ScreenCapturer.EncodePngFromPixelsAsync(pixels, width, height);
        }

        var base64 = Convert.ToBase64String(pngBytes);

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(new
            {
                width,
                height,
                format = "png",
                encoding = "base64",
                mode,
                data = base64
            }, RakaJson.Options)
        };
    }

    private RakaResponse HandleAddXaml(JsonElement? parameters)
    {
        if (!parameters.HasValue)
            return new RakaResponse { Success = false, Error = "Missing parameters" };

        if (!parameters.Value.TryGetProperty("parent", out var parentProp))
            return new RakaResponse { Success = false, Error = "Missing 'parent' element ID" };
        if (!parameters.Value.TryGetProperty("xaml", out var xamlProp))
            return new RakaResponse { Success = false, Error = "Missing 'xaml' parameter" };

        var parentId = parentProp.GetString()!;
        var xaml = xamlProp.GetString()!;
        int? index = null;
        if (parameters.Value.TryGetProperty("index", out var indexProp))
            index = indexProp.GetInt32();

        var parent = _walker.GetElement(parentId)
            ?? throw new ArgumentException($"Element '{parentId}' not found. Run 'inspect' first.");

        var parsed = ParseXaml(xaml);
        if (parsed is not UIElement uiElement)
            return new RakaResponse { Success = false, Error = $"Parsed XAML produced {parsed.GetType().Name}, expected a UIElement" };

        if (parent is Panel panel)
        {
            if (index.HasValue)
                panel.Children.Insert(index.Value, uiElement);
            else
                panel.Children.Add(uiElement);
        }
        else if (parent is ContentControl cc)
        {
            cc.Content = uiElement;
        }
        else if (parent is Border border)
        {
            border.Child = uiElement;
        }
        else if (parent is Viewbox viewbox)
        {
            viewbox.Child = uiElement;
        }
        else
        {
            return new RakaResponse { Success = false, Error = $"Cannot add children to {parent.GetType().Name}. Use a Panel, ContentControl, or Border." };
        }

        // Walk the new element so it gets an ID
        var node = _walker.WalkFrom(uiElement, 2);

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(node, RakaJson.Options)
        };
    }

    private RakaResponse HandleRemove(JsonElement? parameters)
    {
        if (!parameters.HasValue || !parameters.Value.TryGetProperty("element", out var elemProp))
            return new RakaResponse { Success = false, Error = "Missing 'element' parameter" };

        var elementId = elemProp.GetString()!;
        var element = _walker.GetElement(elementId)
            ?? throw new ArgumentException($"Element '{elementId}' not found. Run 'inspect' first.");

        if (element is not UIElement uiElement)
            return new RakaResponse { Success = false, Error = $"Element {elementId} ({element.GetType().Name}) is not a UIElement" };

        var parent = VisualTreeHelper.GetParent(element);
        if (parent == null)
            return new RakaResponse { Success = false, Error = "Cannot remove root element" };

        string parentType = parent.GetType().Name;
        if (parent is Panel panel)
        {
            panel.Children.Remove(uiElement);
        }
        else if (parent is ContentControl cc)
        {
            cc.Content = null;
        }
        else if (parent is Border border)
        {
            border.Child = null;
        }
        else if (parent is Viewbox viewbox)
        {
            viewbox.Child = null;
        }
        else
        {
            return new RakaResponse { Success = false, Error = $"Cannot remove from {parentType}. Parent is not a Panel, ContentControl, or Border." };
        }

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(new { removed = elementId, from = parentType }, RakaJson.Options)
        };
    }

    private RakaResponse HandleReplace(JsonElement? parameters)
    {
        if (!parameters.HasValue)
            return new RakaResponse { Success = false, Error = "Missing parameters" };

        if (!parameters.Value.TryGetProperty("element", out var elemProp))
            return new RakaResponse { Success = false, Error = "Missing 'element' parameter" };
        if (!parameters.Value.TryGetProperty("xaml", out var xamlProp))
            return new RakaResponse { Success = false, Error = "Missing 'xaml' parameter" };

        var elementId = elemProp.GetString()!;
        var xaml = xamlProp.GetString()!;

        var element = _walker.GetElement(elementId)
            ?? throw new ArgumentException($"Element '{elementId}' not found. Run 'inspect' first.");

        if (element is not UIElement uiElement)
            return new RakaResponse { Success = false, Error = $"Element {elementId} ({element.GetType().Name}) is not a UIElement" };

        var parent = VisualTreeHelper.GetParent(element);
        if (parent == null)
            return new RakaResponse { Success = false, Error = "Cannot replace root element" };

        var parsed = ParseXaml(xaml);
        if (parsed is not UIElement newElement)
            return new RakaResponse { Success = false, Error = $"Parsed XAML produced {parsed.GetType().Name}, expected a UIElement" };

        if (parent is Panel panel)
        {
            var idx = panel.Children.IndexOf(uiElement);
            panel.Children.RemoveAt(idx);
            panel.Children.Insert(idx, newElement);
        }
        else if (parent is ContentControl cc)
        {
            cc.Content = newElement;
        }
        else if (parent is Border border)
        {
            border.Child = newElement;
        }
        else if (parent is Viewbox viewbox)
        {
            viewbox.Child = newElement;
        }
        else
        {
            return new RakaResponse { Success = false, Error = $"Cannot replace in {parent.GetType().Name}" };
        }

        var node = _walker.WalkFrom(newElement, 2);

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(node, RakaJson.Options)
        };
    }

    /// <summary>
    /// Parses a XAML string into a UIElement. Adds default namespace declarations if missing.
    /// </summary>
    private static DependencyObject ParseXaml(string xaml)
    {
        // If the XAML doesn't have xmlns, wrap with default WinUI namespaces
        if (!xaml.Contains("xmlns"))
        {
            // Find the first tag name to inject namespaces
            var firstGt = xaml.IndexOf('>');
            var firstSpace = xaml.IndexOf(' ');
            int insertPos;

            if (firstSpace > 0 && firstSpace < firstGt)
                insertPos = firstSpace;
            else if (firstGt > 0)
                insertPos = firstGt;
            else
                throw new ArgumentException("Invalid XAML: no closing '>' found");

            var ns = " xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"" +
                     " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
            xaml = xaml.Insert(insertPos, ns);
        }

        return (DependencyObject)XamlReader.Load(xaml);
    }

    private DependencyObject? GetRoot()
    {
        return _window?.Content as DependencyObject;
    }
}
