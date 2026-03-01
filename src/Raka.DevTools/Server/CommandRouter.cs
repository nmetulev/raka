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
    private readonly XamlReconciler _reconciler = new();
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
                Commands.Status => HandleStatus(),
                Commands.Navigate => HandleNavigate(request.Params),
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

        string? type = null, name = null, text = null, automationId = null, className = null, property = null;
        bool interactive = false, visibleOnly = false;

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
            if (parameters.Value.TryGetProperty("className", out var classProp))
                className = classProp.GetString();
            if (parameters.Value.TryGetProperty("interactive", out var interProp))
                interactive = interProp.GetBoolean();
            if (parameters.Value.TryGetProperty("visibleOnly", out var visProp))
                visibleOnly = visProp.GetBoolean();
            if (parameters.Value.TryGetProperty("property", out var propProp))
                property = propProp.GetString();
        }

        if (type == null && name == null && text == null && automationId == null &&
            className == null && !interactive && !visibleOnly && property == null)
            return new RakaResponse { Success = false, Error = "Specify at least one search criterion: type, name, text, automationId, className, interactive, visibleOnly, or property" };

        var results = _walker.Search(root, type, name, text, automationId, className, interactive, visibleOnly, property);

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
        if (!parameters.HasValue)
            return new RakaResponse { Success = false, Error = "Missing parameters" };

        DependencyObject? element = null;
        string elementId = "";

        // Primary: resolve by element ID
        if (parameters.Value.TryGetProperty("element", out var elemProp) && elemProp.GetString() is string eid)
        {
            elementId = eid;
            element = _walker.GetElement(elementId)
                ?? throw new ArgumentException($"Element '{elementId}' not found. Run 'inspect' first.");
        }
        // Alternative: resolve by x:Name
        else if (parameters.Value.TryGetProperty("name", out var nameProp) && nameProp.GetString() is string xname)
        {
            var root = GetRoot();
            if (root == null) return new RakaResponse { Success = false, Error = "No window content available" };

            string? type = parameters.Value.TryGetProperty("type", out var tp) ? tp.GetString() : null;
            var results = _walker.Search(root, type, xname, null, null);
            if (results.Count == 0)
                return new RakaResponse { Success = false, Error = $"No element found with name '{xname}'" };
            elementId = results[0].Id;
            element = _walker.GetElement(elementId)!;
        }
        // Alternative: resolve by type + text
        else if (parameters.Value.TryGetProperty("type", out var typeProp) && typeProp.GetString() is string tname)
        {
            var root = GetRoot();
            if (root == null) return new RakaResponse { Success = false, Error = "No window content available" };

            string? text = parameters.Value.TryGetProperty("text", out var txp) ? txp.GetString() : null;
            var results = _walker.Search(root, tname, null, text, null);
            if (results.Count == 0)
                return new RakaResponse { Success = false, Error = $"No element found matching type '{tname}'" + (text != null ? $" with text '{text}'" : "") };
            elementId = results[0].Id;
            element = _walker.GetElement(elementId)!;
        }
        else
        {
            return new RakaResponse { Success = false, Error = "Missing 'element', 'name', or 'type' parameter" };
        }

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

        // Special handling for NavigationViewItem: go through NavigationView's selection model
        // ISelectionItemProvider.Select() only updates visual state but may not fire SelectionChanged
        if (element is NavigationViewItem navItem)
        {
            var navView = FindParent<NavigationView>(navItem);
            if (navView != null)
            {
                // Force selection change by clearing first, then setting
                var previousItem = navView.SelectedItem;
                if (!ReferenceEquals(previousItem, navItem))
                {
                    navView.SelectedItem = null;
                }
                navView.SelectedItem = navItem;
                return new RakaResponse
                {
                    Success = true,
                    Data = JsonSerializer.SerializeToElement(new { action = "select", element = elementId, type = element.GetType().Name }, RakaJson.Options)
                };
            }
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

        return new RakaResponse { Success = false, Error = $"{element.GetType().Name} does not support click, toggle, or select. Try: search -t Button (or NavigationViewItem, CheckBox, ToggleSwitch) to find interactive children." };
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
        // Exception: Mica/Acrylic backdrops produce black images in capture mode
        string? hint = null;
        if (mode == "auto")
        {
            if (elementId != null)
            {
                mode = "render";
            }
            else if (_window?.SystemBackdrop != null)
            {
                mode = "render";
                if (background == null)
                {
                    // Pick background based on actual theme
                    var isDark = (_window.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
                    background = isDark ? "#1E1E1E" : "#F3F3F3";
                }
                hint = "Auto-switched to render mode (Mica/Acrylic backdrop detected). " +
                       "Use --mode capture to override, or --bg to change background color.";
            }
            else
            {
                mode = "capture";
            }
        }
        else if (mode == "capture" && elementId == null && _window?.SystemBackdrop != null)
        {
            hint = "Warning: Mica/Acrylic backdrop detected — capture mode may produce a black image. " +
                   "Try --mode render --bg \"#1E1E1E\" (dark) or \"#F3F3F3\" (light) for reliable results.";
        }

        // Allow the layout pass to complete after any recent XAML changes
        await Task.Delay(50);

        byte[] pngBytes;
        int width, height;

        try
        {
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
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            return new RakaResponse
            {
                Success = false,
                Error = $"Screenshot failed (COM error 0x{ex.HResult:X8}): {ex.Message}. " +
                        "If you just injected XAML, wait a moment and retry."
            };
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
                hint,
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
        else if (parent is ContentPresenter cpAdd)
        {
            cpAdd.Content = uiElement;
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
        else if (parent is ContentPresenter cpRem)
        {
            cpRem.Content = null;
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

        // Try React-style reconciliation first (diffs old vs new XAML, patches in place)
        var (reconciled, patchCount, reconcileError) = _reconciler.TryReconcile(element, elementId, xaml);
        if (reconciled)
        {
            // Success — tree was patched in place, all runtime state preserved
            var node = _walker.WalkFrom(uiElement, 2);
            var reconcileData = new Dictionary<string, object?>
            {
                ["id"] = node.Id,
                ["type"] = node.Type,
                ["childCount"] = node.ChildCount,
                ["patches"] = patchCount,
                ["mode"] = "reconcile"
            };
            return new RakaResponse
            {
                Success = true,
                Data = JsonSerializer.SerializeToElement(reconcileData, RakaJson.Options)
            };
        }

        // Reconciliation failed (structural change or first load) — fall back to full replacement
        _reconciler.CacheXaml(elementId, xaml);

        var parent = VisualTreeHelper.GetParent(element);
        if (parent == null)
        {
            if (_window != null)
            {
                var parsed2 = ParseXaml(xaml);
                if (parsed2 is not UIElement newRoot)
                    return new RakaResponse { Success = false, Error = $"Parsed XAML produced {parsed2.GetType().Name}, expected a UIElement" };

                _window.Content = newRoot;
                var rootNode = _walker.WalkFrom(newRoot, 2);
                var dataDict2 = new Dictionary<string, object?>
                {
                    ["id"] = rootNode.Id,
                    ["type"] = rootNode.Type,
                    ["childCount"] = rootNode.ChildCount,
                    ["mode"] = "replace",
                    ["reconcileError"] = reconcileError
                };
                return new RakaResponse
                {
                    Success = true,
                    Data = JsonSerializer.SerializeToElement(dataDict2, RakaJson.Options)
                };
            }
            return new RakaResponse { Success = false, Error = "Cannot replace root element — no window reference" };
        }

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
        else if (parent is ContentPresenter cp)
        {
            cp.Content = newElement;
        }
        else
        {
            return new RakaResponse { Success = false, Error = $"Cannot replace in {parent.GetType().Name}" };
        }

        var node2 = _walker.WalkFrom(newElement, 2);
        var dataDict = new Dictionary<string, object?>
        {
            ["id"] = node2.Id,
            ["type"] = node2.Type,
            ["childCount"] = node2.ChildCount,
            ["mode"] = "replace",
            ["reconcileError"] = reconcileError
        };
        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(dataDict, RakaJson.Options)
        };
    }

    /// <summary>
    /// Parses a XAML string into a UIElement. Adds default namespace declarations if missing.
    /// </summary>
    private static DependencyObject ParseXaml(string xaml)
    {
        // If the XAML doesn't have xmlns, wrap with default WinUI namespaces
        if (!xaml.Contains("xmlns=\""))
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

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T match) return match;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private RakaResponse HandleStatus()
    {
        var title = _window?.Title;
        var width = _window?.AppWindow?.Size.Width;
        var height = _window?.AppWindow?.Size.Height;

        // Find the current page by looking for Frame elements
        string? currentPage = null;
        string? theme = null;
        int elementCount = 0;

        var root = GetRoot();
        if (root != null)
        {
            CountAndDiscover(root, ref elementCount, ref currentPage);
        }

        if (root is FrameworkElement rootFe)
        {
            theme = rootFe.ActualTheme.ToString();
        }

        string? backdropType = _window?.SystemBackdrop?.GetType().Name;

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(new
            {
                title,
                width,
                height,
                theme,
                currentPage,
                backdropType,
                elementCount
            }, RakaJson.Options)
        };
    }

    private static void CountAndDiscover(DependencyObject obj, ref int count, ref string? currentPage)
    {
        count++;
        if (obj is Microsoft.UI.Xaml.Controls.Frame frame && frame.Content != null)
        {
            currentPage = frame.Content.GetType().FullName;
        }
        int childCount = VisualTreeHelper.GetChildrenCount(obj);
        for (int i = 0; i < childCount; i++)
        {
            CountAndDiscover(VisualTreeHelper.GetChild(obj, i), ref count, ref currentPage);
        }
    }

    private RakaResponse HandleNavigate(JsonElement? parameters)
    {
        if (!parameters.HasValue || !parameters.Value.TryGetProperty("page", out var pageProp))
            return new RakaResponse { Success = false, Error = "Missing 'page' parameter" };

        var pageName = pageProp.GetString()!;

        var root = GetRoot();
        if (root == null)
            return new RakaResponse { Success = false, Error = "No window content available" };

        // Find Frame elements in the tree
        var frame = FindFrame(root);
        if (frame == null)
            return new RakaResponse { Success = false, Error = "No Frame element found in the visual tree" };

        // Resolve the page type by searching loaded assemblies
        Type? pageType = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // Try exact match first
            pageType = assembly.GetType(pageName);
            if (pageType != null) break;

            // Try partial match (just the class name without namespace)
            foreach (var type in assembly.GetTypes())
            {
                if (type.Name.Equals(pageName, StringComparison.OrdinalIgnoreCase) &&
                    typeof(Microsoft.UI.Xaml.Controls.Page).IsAssignableFrom(type))
                {
                    pageType = type;
                    break;
                }
            }
            if (pageType != null) break;
        }

        if (pageType == null)
            return new RakaResponse { Success = false, Error = $"Page type '{pageName}' not found. Use the full type name (e.g., MyApp.Pages.SettingsPage) or just the class name." };

        frame.Navigate(pageType);

        // Also update NavigationView selection if one exists
        var navView = FindDescendant<NavigationView>(root);
        if (navView != null)
        {
            foreach (var menuItem in navView.MenuItems)
            {
                if (menuItem is NavigationViewItem navItem)
                {
                    var tag = navItem.Tag?.ToString();
                    if (tag != null && pageType.Name.Contains(tag, StringComparison.OrdinalIgnoreCase))
                    {
                        navView.SelectedItem = navItem;
                        break;
                    }
                }
            }
            foreach (var menuItem in navView.FooterMenuItems)
            {
                if (menuItem is NavigationViewItem navItem)
                {
                    var tag = navItem.Tag?.ToString();
                    if (tag != null && pageType.Name.Contains(tag, StringComparison.OrdinalIgnoreCase))
                    {
                        navView.SelectedItem = navItem;
                        break;
                    }
                }
            }
        }

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(new
            {
                navigated = true,
                page = pageType.FullName,
                frame = frame.Name ?? "(unnamed)"
            }, RakaJson.Options)
        };
    }

    private static Microsoft.UI.Xaml.Controls.Frame? FindFrame(DependencyObject obj)
    {
        if (obj is Microsoft.UI.Xaml.Controls.Frame frame) return frame;
        int childCount = VisualTreeHelper.GetChildrenCount(obj);
        for (int i = 0; i < childCount; i++)
        {
            var result = FindFrame(VisualTreeHelper.GetChild(obj, i));
            if (result != null) return result;
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject obj) where T : DependencyObject
    {
        if (obj is T match) return match;
        int childCount = VisualTreeHelper.GetChildrenCount(obj);
        for (int i = 0; i < childCount; i++)
        {
            var result = FindDescendant<T>(VisualTreeHelper.GetChild(obj, i));
            if (result != null) return result;
        }
        return null;
    }
}
