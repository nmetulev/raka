using System.Runtime.InteropServices.WindowsRuntime;
using System.Linq;
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
                Commands.Invoke => HandleInvoke(request.Params),
                Commands.Click => await HandleClickAsync(request.Params),
                Commands.Screenshot => await HandleScreenshotAsync(request.Params),
                Commands.AddXaml => HandleAddXaml(request.Params),
                Commands.RemoveElement => HandleRemove(request.Params),
                Commands.ReplaceXaml => HandleReplace(request.Params),
                Commands.Status => HandleStatus(),
                Commands.Navigate => HandleNavigate(request.Params),
                Commands.ListPages => HandleListPages(),
                Commands.Type => await HandleTypeAsync(request.Params),
                Commands.Hotkey => await HandleHotkeyAsync(request.Params),
                Commands.GetStates => HandleGetStates(request.Params),
                Commands.SetState => HandleSetState(request.Params),
                Commands.Styles => HandleStyles(request.Params),
                Commands.Resources => HandleResources(request.Params),
                Commands.SetResource => HandleSetResource(request.Params),
                _ => new RakaResponse { Success = false, Error = $"Unknown command: {request.Command}" }
            };
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException ?? ex;
            var frame = inner.StackTrace?.Split('\n').FirstOrDefault(l => !l.Contains("CommandRouter"))?.Trim();
            return new RakaResponse 
            { 
                Success = false, 
                Error = $"{inner.GetType().Name}: {inner.Message}" + (frame != null ? $"\n  at {frame}" : "")
            };
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

        if (!parameters.Value.TryGetProperty("property", out var propProp))
            return new RakaResponse { Success = false, Error = "Missing 'property' parameter" };
        if (!parameters.Value.TryGetProperty("value", out var valProp))
            return new RakaResponse { Success = false, Error = "Missing 'value' parameter" };

        var propertyName = propProp.GetString()!;
        var valueStr = valProp.GetString()!;

        DependencyObject? element = null;
        string elementId = "";

        if (parameters.Value.TryGetProperty("element", out var elemProp) && elemProp.GetString() is string eid)
        {
            elementId = eid;
            element = _walker.GetElement(elementId)
                ?? throw new ArgumentException($"Element '{elementId}' not found. Run 'inspect' first.");
        }
        else if (parameters.Value.TryGetProperty("name", out var nameProp) && nameProp.GetString() is string xname)
        {
            var root = GetRoot();
            if (root == null) return new RakaResponse { Success = false, Error = "No window content available" };

            var results = _walker.Search(root, null, xname, null, null);
            if (results.Count == 0)
                return new RakaResponse { Success = false, Error = $"No element found with name '{xname}'" };
            elementId = results[0].Id;
            element = _walker.GetElement(elementId)!;
        }
        else
        {
            return new RakaResponse { Success = false, Error = "Missing 'element' or 'name' parameter" };
        }

        PropertyWriter.SetProperty(element, propertyName, valueStr);

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

    private RakaResponse HandleInvoke(JsonElement? parameters)
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
            try { invoker.Invoke(); }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                return new RakaResponse { Success = false, Error = $"Click on {element.GetType().Name} (invoke) failed — {inner.GetType().Name}: {inner.Message}" };
            }
            return new RakaResponse
            {
                Success = true,
                Data = JsonSerializer.SerializeToElement(new { action = "invoke", element = elementId, type = element.GetType().Name }, RakaJson.Options)
            };
        }

        // Try Toggle (checkboxes, toggle switches, toggle buttons)
        if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggler)
        {
            try { toggler.Toggle(); }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                return new RakaResponse { Success = false, Error = $"Click on {element.GetType().Name} (toggle) failed — {inner.GetType().Name}: {inner.Message}" };
            }
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
                try
                {
                    // Force selection change by clearing first, then setting
                    var previousItem = navView.SelectedItem;
                    if (!ReferenceEquals(previousItem, navItem))
                    {
                        navView.SelectedItem = null;
                    }
                    navView.SelectedItem = navItem;
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException ?? ex;
                    return new RakaResponse { Success = false, Error = $"Click on {element.GetType().Name} (select) failed — {inner.GetType().Name}: {inner.Message}" };
                }
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
            try { selector.Select(); }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                return new RakaResponse { Success = false, Error = $"Click on {element.GetType().Name} (select) failed — {inner.GetType().Name}: {inner.Message}" };
            }
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
        string? state = null;

        if (parameters.HasValue)
        {
            if (parameters.Value.TryGetProperty("element", out var elemProp))
                elementId = elemProp.GetString();
            if (parameters.Value.TryGetProperty("mode", out var modeProp))
                mode = modeProp.GetString() ?? "auto";
            if (parameters.Value.TryGetProperty("background", out var bgProp))
                background = bgProp.GetString();
            if (parameters.Value.TryGetProperty("state", out var stateProp))
                state = stateProp.GetString();
        }

        // If --state is specified, apply the visual state before capturing
        Control? stateTarget = null;
        string? previousState = null;
        if (state != null && elementId != null)
        {
            var el = _walker.GetElement(elementId);
            if (el is Control ctrl)
            {
                stateTarget = ctrl;
                // Remember current state for revert
                var groups = VisualStateManager.GetVisualStateGroups(ctrl);
                foreach (var g in groups)
                {
                    foreach (var s in g.States)
                    {
                        if (s.Name == state)
                        {
                            previousState = g.CurrentState?.Name;
                            break;
                        }
                    }
                    if (previousState != null) break;
                }
                VisualStateManager.GoToState(ctrl, state, true);
                await Task.Delay(100); // Let the state transition animate
            }
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

        // Revert visual state if we applied one
        if (stateTarget != null && previousState != null)
        {
            VisualStateManager.GoToState(stateTarget, previousState, true);
        }
        else if (stateTarget != null)
        {
            // No previous state recorded, go to "Normal" as safe default
            VisualStateManager.GoToState(stateTarget, "Normal", true);
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

        try
        {
            frame.Navigate(pageType);
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException ?? ex;
            return new RakaResponse { Success = false, Error = $"Navigation to '{pageName}' failed — {inner.GetType().Name}: {inner.Message}" };
        }

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

    private RakaResponse HandleListPages()
    {
        var pages = new List<Dictionary<string, object?>>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (typeof(Microsoft.UI.Xaml.Controls.Page).IsAssignableFrom(type) && !type.IsAbstract)
                    {
                        pages.Add(new Dictionary<string, object?>
                        {
                            ["fullName"] = type.FullName,
                            ["name"] = type.Name,
                            ["assembly"] = assembly.GetName().Name
                        });
                    }
                }
            }
            catch { /* ReflectionTypeLoadException for some assemblies */ }
        }

        // Sort by name for readability
        pages.Sort((a, b) => string.Compare(a["name"]?.ToString(), b["name"]?.ToString(), StringComparison.OrdinalIgnoreCase));

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(pages, RakaJson.Options)
        };
    }

    private async Task<RakaResponse> HandleTypeAsync(JsonElement? parameters)
    {
        if (!parameters.HasValue)
            return new RakaResponse { Success = false, Error = "Missing parameters" };

        if (!parameters.Value.TryGetProperty("text", out var textProp))
            return new RakaResponse { Success = false, Error = "Missing 'text' parameter" };

        var text = textProp.GetString()!;

        DependencyObject? element = null;
        string elementId = "";

        if (parameters.Value.TryGetProperty("element", out var elemProp) && elemProp.GetString() is string eid)
        {
            elementId = eid;
            element = _walker.GetElement(elementId)
                ?? throw new ArgumentException($"Element '{elementId}' not found. Run 'inspect' first.");
        }
        else if (parameters.Value.TryGetProperty("name", out var nameProp) && nameProp.GetString() is string xname)
        {
            var root = GetRoot();
            if (root == null) return new RakaResponse { Success = false, Error = "No window content available" };

            var results = _walker.Search(root, null, xname, null, null);
            if (results.Count == 0)
                return new RakaResponse { Success = false, Error = $"No element found with name '{xname}'" };
            elementId = results[0].Id;
            element = _walker.GetElement(elementId)!;
        }
        else
        {
            return new RakaResponse { Success = false, Error = "Missing 'element' or 'name' parameter" };
        }

        if (element is not UIElement uiElement)
            return new RakaResponse { Success = false, Error = $"{element.GetType().Name} is not a UIElement — cannot focus for typing" };

        // Focus the element on the UI thread
        if (uiElement is Control ctrl)
            ctrl.Focus(FocusState.Programmatic);
        else
            uiElement.Focus(FocusState.Programmatic);

        // Small delay to let focus take effect
        await Task.Delay(50);

        // Send real keystrokes from background thread — yields UI thread so it can process the WM_CHAR messages
        int delay = 30;
        if (parameters.Value.TryGetProperty("delay", out var delayProp) && delayProp.TryGetInt32(out int d))
            delay = d;

        await Task.Run(() => InputSimulator.SendKeysAsync(text, delay));

        // Wait for the last keystroke to be processed
        await Task.Delay(50);

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["action"] = "type",
                ["element"] = elementId,
                ["type"] = element.GetType().Name,
                ["text"] = text,
                ["method"] = "sendInput"
            }, RakaJson.Options)
        };
    }

    private async Task<RakaResponse> HandleHotkeyAsync(JsonElement? parameters)
    {
        if (!parameters.HasValue)
            return new RakaResponse { Success = false, Error = "Missing parameters" };

        if (!parameters.Value.TryGetProperty("keys", out var keysProp) || keysProp.GetString() is not string keys)
            return new RakaResponse { Success = false, Error = "Missing 'keys' parameter" };

        // Parse key combination: "Ctrl+S", "Alt+F4", "Shift+Tab", "Tab", "Enter"
        var parts = keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return new RakaResponse { Success = false, Error = "Empty key combination" };

        var modifiers = new List<ushort>();
        ushort mainKey = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            bool isLast = i == parts.Length - 1;

            if (!isLast || IsModifier(part))
            {
                ushort mod = ParseModifier(part);
                if (mod == 0)
                    return new RakaResponse { Success = false, Error = $"Unknown modifier: '{part}'. Use Ctrl, Alt, Shift, or Win." };
                modifiers.Add(mod);
            }
            else
            {
                mainKey = ParseKey(part);
                if (mainKey == 0)
                    return new RakaResponse { Success = false, Error = $"Unknown key: '{part}'. Use key names like Tab, Enter, Escape, F1-F12, or single characters." };
            }
        }

        if (mainKey == 0 && modifiers.Count > 0)
        {
            // Lone modifier press (e.g., just "Alt") — treat last modifier as main key
            mainKey = modifiers[^1];
            modifiers.RemoveAt(modifiers.Count - 1);
        }

        await Task.Run(() => InputSimulator.SendHotkey(modifiers.ToArray(), mainKey));
        await Task.Delay(50);

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["action"] = "hotkey",
                ["keys"] = keys,
                ["modifiers"] = modifiers.Select(m => $"0x{m:X2}").ToArray(),
                ["mainKey"] = $"0x{mainKey:X2}",
                ["method"] = "sendInput"
            }, RakaJson.Options)
        };
    }

    private static bool IsModifier(string key) =>
        key.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Control", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Shift", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Windows", StringComparison.OrdinalIgnoreCase);

    private static ushort ParseModifier(string key) => key.ToLowerInvariant() switch
    {
        "ctrl" or "control" => 0x11, // VK_CONTROL
        "alt" => 0x12,               // VK_MENU
        "shift" => 0x10,             // VK_SHIFT
        "win" or "windows" => 0x5B,  // VK_LWIN
        _ => 0
    };

    private static ushort ParseKey(string key)
    {
        // Single character
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z') return (ushort)c;
            if (c is >= '0' and <= '9') return (ushort)c;
            return 0;
        }

        return key.ToLowerInvariant() switch
        {
            "tab" => 0x09,
            "enter" or "return" => 0x0D,
            "escape" or "esc" => 0x1B,
            "space" => 0x20,
            "backspace" or "back" => 0x08,
            "delete" or "del" => 0x2E,
            "insert" or "ins" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73,
            "f5" => 0x74, "f6" => 0x75, "f7" => 0x76, "f8" => 0x77,
            "f9" => 0x78, "f10" => 0x79, "f11" => 0x7A, "f12" => 0x7B,
            _ => 0
        };
    }

    private RakaResponse HandleGetStates(JsonElement? parameters)
    {
        if (!parameters.HasValue)
            return new RakaResponse { Success = false, Error = "Missing parameters" };

        var element = ResolveElementFromParams(parameters.Value);
        if (element == null)
            return new RakaResponse { Success = false, Error = "Element not found" };

        // GetVisualStateGroups needs the template root, not the Control itself.
        // Try the control first, then walk into its first visual child (template root).
        var result = new List<Dictionary<string, object?>>();
        var targets = new List<DependencyObject> { element };
        if (element is Control && VisualTreeHelper.GetChildrenCount(element) > 0)
            targets.Add(VisualTreeHelper.GetChild(element, 0));

        foreach (var target in targets)
        {
            if (target is not FrameworkElement fe) continue;
            var groups = VisualStateManager.GetVisualStateGroups(fe);
            foreach (var group in groups)
            {
                var states = new List<Dictionary<string, object?>>();
                foreach (var state in group.States)
                {
                    states.Add(new Dictionary<string, object?>
                    {
                        ["name"] = state.Name,
                        ["hasStoryboard"] = state.Storyboard != null
                    });
                }

                result.Add(new Dictionary<string, object?>
                {
                    ["groupName"] = group.Name,
                    ["currentState"] = group.CurrentState?.Name,
                    ["states"] = states
                });
            }
            if (result.Count > 0) break; // Found groups, don't duplicate
        }

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(result, RakaJson.Options)
        };
    }

    private RakaResponse HandleSetState(JsonElement? parameters)
    {
        if (!parameters.HasValue)
            return new RakaResponse { Success = false, Error = "Missing parameters" };

        if (!parameters.Value.TryGetProperty("state", out var stateProp) || stateProp.GetString() is not string stateName)
            return new RakaResponse { Success = false, Error = "Missing 'state' parameter" };

        var element = ResolveElementFromParams(parameters.Value);
        if (element == null)
            return new RakaResponse { Success = false, Error = "Element not found" };

        if (element is not Control control)
            return new RakaResponse { Success = false, Error = $"{element.GetType().Name} is not a Control — visual states require a Control" };

        bool success = VisualStateManager.GoToState(control, stateName, true);

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["action"] = "set-state",
                ["element"] = GetElementId(element),
                ["type"] = element.GetType().Name,
                ["state"] = stateName,
                ["applied"] = success
            }, RakaJson.Options)
        };
    }

    private DependencyObject? ResolveElementFromParams(JsonElement parameters)
    {
        if (parameters.TryGetProperty("element", out var elemProp) && elemProp.GetString() is string eid)
            return _walker.GetElement(eid);

        if (parameters.TryGetProperty("name", out var nameProp) && nameProp.GetString() is string xname)
        {
            var root = GetRoot();
            if (root == null) return null;
            var results = _walker.Search(root, null, xname, null, null);
            return results.Count > 0 ? _walker.GetElement(results[0].Id) : null;
        }

        return null;
    }

    private string GetElementId(DependencyObject element)
    {
        // Reverse lookup through the walker's cache
        var root = GetRoot();
        if (root == null) return "?";
        // Do a search-based lookup by reference
        return _walker.GetIdForElement(element) ?? "?";
    }

    private async Task<RakaResponse> HandleClickAsync(JsonElement? parameters)
    {
        if (!parameters.HasValue)
            return new RakaResponse { Success = false, Error = "Missing parameters" };

        DependencyObject? element = null;
        string elementId = "";

        // Resolve element (same logic as invoke)
        if (parameters.Value.TryGetProperty("element", out var elemProp) && elemProp.GetString() is string eid)
        {
            elementId = eid;
            element = _walker.GetElement(elementId)
                ?? throw new ArgumentException($"Element '{elementId}' not found. Run 'inspect' first.");
        }
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

        if (element is not FrameworkElement fe)
            return new RakaResponse { Success = false, Error = $"Element {elementId} ({element.GetType().Name}) is not a FrameworkElement — cannot determine bounds for click" };

        // Get the element's screen coordinates
        var transform = fe.TransformToVisual(null);
        var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
        var bounds = new Windows.Foundation.Rect(point, new Windows.Foundation.Size(fe.ActualWidth, fe.ActualHeight));

        // Get window position to convert to screen coordinates
        if (_window == null)
            return new RakaResponse { Success = false, Error = "No window available" };

        var appWindow = _window.AppWindow;
        var windowPos = appWindow.Position;
        double scale = GetScaleFactor();

        int screenX = (int)(windowPos.X + bounds.X * scale + bounds.Width * scale / 2);
        int screenY = (int)(windowPos.Y + bounds.Y * scale + bounds.Height * scale / 2);

        // Send real mouse click from background thread
        await Task.Run(() => InputSimulator.SendClick(screenX, screenY));

        // Wait for click to be processed
        await Task.Delay(100);

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["action"] = "click",
                ["element"] = elementId,
                ["type"] = element.GetType().Name,
                ["screenX"] = screenX,
                ["screenY"] = screenY,
                ["method"] = "sendInput"
            }, RakaJson.Options)
        };
    }

    private double GetScaleFactor()
    {
        if (_window?.Content is FrameworkElement fe)
        {
            var xScale = fe.XamlRoot?.RasterizationScale ?? 1.0;
            return xScale;
        }
        return 1.0;
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

    private RakaResponse HandleStyles(JsonElement? parameters)
    {
        if (!parameters.HasValue)
            return new RakaResponse { Success = false, Error = "Missing parameters" };

        DependencyObject? element = null;
        string elementId = "";

        if (parameters.Value.TryGetProperty("element", out var elemProp) && elemProp.GetString() is string eid)
        {
            elementId = eid;
            element = _walker.GetElement(elementId)
                ?? throw new ArgumentException($"Element '{elementId}' not found. Run 'inspect' first.");
        }
        else if (parameters.Value.TryGetProperty("name", out var nameProp) && nameProp.GetString() is string xname)
        {
            var root = GetRoot();
            if (root == null) return new RakaResponse { Success = false, Error = "No window content available" };
            var results = _walker.Search(root, null, xname, null, null);
            if (results.Count == 0)
                return new RakaResponse { Success = false, Error = $"No element found with name '{xname}'" };
            elementId = results[0].Id;
            element = _walker.GetElement(elementId)!;
        }
        else
        {
            return new RakaResponse { Success = false, Error = "Missing 'element' or 'name' parameter" };
        }

        if (element is not FrameworkElement fe)
            return new RakaResponse { Success = false, Error = $"Element {elementId} ({element.GetType().Name}) is not a FrameworkElement" };

        var data = new Dictionary<string, object?>
        {
            ["element"] = elementId,
            ["type"] = fe.GetType().Name
        };

        var style = fe.Style;
        if (style != null)
        {
            var setters = new List<Dictionary<string, string?>>();
            foreach (var setterBase in style.Setters)
            {
                if (setterBase is Setter setter)
                {
                    setters.Add(new Dictionary<string, string?>
                    {
                        ["property"] = GetDependencyPropertyName(setter.Property, style.TargetType),
                        ["value"] = PropertyReader.FormatValue(setter.Value)
                    });
                }
            }
            data["style"] = new Dictionary<string, object?>
            {
                ["targetType"] = style.TargetType?.Name,
                ["setterCount"] = style.Setters.Count,
                ["setters"] = setters,
                ["basedOn"] = style.BasedOn?.TargetType?.Name
            };
        }
        else
        {
            data["style"] = null;
        }

        // Also show implicit style from resources if no explicit style
        if (style == null)
        {
            data["note"] = "No explicit Style set. Element uses default implicit style.";
        }

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(data, RakaJson.Options)
        };
    }

    private RakaResponse HandleResources(JsonElement? parameters)
    {
        string scope = "all";
        string? filter = null;
        string? theme = null;
        string? elementId = null;

        if (parameters.HasValue)
        {
            if (parameters.Value.TryGetProperty("scope", out var scopeProp))
                scope = scopeProp.GetString() ?? "all";
            if (parameters.Value.TryGetProperty("filter", out var filterProp))
                filter = filterProp.GetString();
            if (parameters.Value.TryGetProperty("theme", out var themeProp))
                theme = themeProp.GetString();
            if (parameters.Value.TryGetProperty("element", out var elemProp))
                elementId = elemProp.GetString();
        }

        var resources = new List<Dictionary<string, string?>>();

        // Element-level resources
        if ((scope == "all" || scope == "element") && elementId != null)
        {
            var element = _walker.GetElement(elementId);
            if (element is FrameworkElement fe && fe.Resources.Count > 0)
                CollectResources(fe.Resources, $"element({elementId})", filter, resources);
        }

        // Walk up the visual tree for page-level resources
        if (scope == "all" || scope == "page")
        {
            var root = GetRoot();
            if (root != null)
            {
                var frame = FindFrame(root);
                if (frame?.Content is FrameworkElement page && page.Resources.Count > 0)
                    CollectResources(page.Resources, "page", filter, resources);
            }
        }

        // App-level resources
        if (scope == "all" || scope == "app")
        {
            var appResources = Application.Current.Resources;
            CollectResources(appResources, "app", filter, resources);

            // Theme dictionaries
            if (appResources.ThemeDictionaries.Count > 0)
            {
                foreach (var kvp in appResources.ThemeDictionaries)
                {
                    var themeName = kvp.Key?.ToString() ?? "unknown";
                    if (theme != null && !themeName.Equals(theme, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (kvp.Value is ResourceDictionary themeDict)
                        CollectResources(themeDict, $"app/theme/{themeName}", filter, resources);
                }
            }
        }

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["count"] = resources.Count,
                ["scope"] = scope,
                ["filter"] = filter,
                ["theme"] = theme,
                ["resources"] = resources
            }, RakaJson.Options)
        };
    }

    private static void CollectResources(ResourceDictionary dict, string scope, string? filter,
        List<Dictionary<string, string?>> results)
    {
        foreach (var key in dict.Keys)
        {
            var keyStr = key?.ToString() ?? "";
            if (filter != null && !keyStr.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var value = dict[key];
                results.Add(new Dictionary<string, string?>
                {
                    ["key"] = keyStr,
                    ["value"] = PropertyReader.FormatValue(value),
                    ["type"] = value?.GetType().Name,
                    ["scope"] = scope
                });
            }
            catch
            {
                results.Add(new Dictionary<string, string?>
                {
                    ["key"] = keyStr,
                    ["value"] = "(error reading)",
                    ["type"] = null,
                    ["scope"] = scope
                });
            }
        }

        // Recurse into merged dictionaries
        foreach (var merged in dict.MergedDictionaries)
        {
            CollectResources(merged, scope + "/merged", filter, results);

            // Also recurse into theme dictionaries within merged dictionaries
            foreach (var tkvp in merged.ThemeDictionaries)
            {
                var tName = tkvp.Key?.ToString() ?? "unknown";
                if (tkvp.Value is ResourceDictionary tDict)
                    CollectResources(tDict, scope + $"/merged/theme/{tName}", filter, results);
            }
        }
    }

    private static ResourceDictionary? FindResourceDict(ResourceDictionary dict, string key, string scopePrefix,
        out object? existingValue, out string scope)
    {
        scope = scopePrefix;
        existingValue = null;

        if (dict.ContainsKey(key))
        {
            existingValue = dict[key];
            return dict;
        }

        // Search theme dictionaries
        foreach (var kvp in dict.ThemeDictionaries)
        {
            if (kvp.Value is ResourceDictionary themeDict && themeDict.ContainsKey(key))
            {
                existingValue = themeDict[key];
                scope = $"{scopePrefix}/theme/{kvp.Key}";
                return themeDict;
            }
        }

        // Recurse into merged dictionaries
        foreach (var merged in dict.MergedDictionaries)
        {
            var result = FindResourceDict(merged, key, scopePrefix + "/merged", out existingValue, out scope);
            if (result != null) return result;
        }

        return null;
    }

    private RakaResponse HandleSetResource(JsonElement? parameters)
    {
        if (!parameters.HasValue)
            return new RakaResponse { Success = false, Error = "Missing parameters" };

        if (!parameters.Value.TryGetProperty("key", out var keyProp))
            return new RakaResponse { Success = false, Error = "Missing 'key' parameter" };
        if (!parameters.Value.TryGetProperty("value", out var valProp))
            return new RakaResponse { Success = false, Error = "Missing 'value' parameter" };

        var key = keyProp.GetString()!;
        var valueStr = valProp.GetString()!;

        string scope = "app";
        if (parameters.Value.TryGetProperty("scope", out var scopeProp))
            scope = scopeProp.GetString() ?? "app";

        // Find the resource dictionary to modify
        ResourceDictionary? targetDict = null;
        object? existingValue = null;

        if (scope == "page")
        {
            var root = GetRoot();
            var frame = root != null ? FindFrame(root) : null;
            if (frame?.Content is FrameworkElement page)
            {
                if (page.Resources.ContainsKey(key))
                {
                    targetDict = page.Resources;
                    existingValue = page.Resources[key];
                }
            }
        }

         // Default: search app resources (including theme dictionaries and merged theme dictionaries)
        if (targetDict == null)
        {
            var appResources = Application.Current.Resources;
            targetDict = FindResourceDict(appResources, key, "app", out existingValue, out scope);
        }

        if (targetDict == null)
            return new RakaResponse { Success = false, Error = $"Resource '{key}' not found in {scope} resources" };

        // Parse the new value based on the existing value's type
        object? newValue;
        try
        {
            newValue = ConvertResourceValue(valueStr, existingValue);
        }
        catch (Exception ex)
        {
            return new RakaResponse { Success = false, Error = $"Cannot convert '{valueStr}' to {existingValue?.GetType().Name}: {ex.Message}" };
        }

        bool apply = parameters.Value.TryGetProperty("apply", out var applyProp) && applyProp.GetBoolean();

        // Always update the resource in the dictionary where it was found
        targetDict[key] = newValue;

        bool applied = false;
        if (apply)
        {
            // Also set at top-level app resources for highest priority on new elements
            Application.Current.Resources[key] = newValue;

            // Reload the current page so new elements pick up the updated resource
            var root = GetRoot();
            var frame = root != null ? FindFrame(root) : null;
            if (frame != null)
            {
                var currentPage = frame.Content?.GetType();
                if (currentPage != null)
                {
                    frame.Navigate(typeof(Page));
                    frame.DispatcherQueue.TryEnqueue(() => frame.Navigate(currentPage));
                    applied = true;
                }
            }
        }

        return new RakaResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["key"] = key,
                ["oldValue"] = PropertyReader.FormatValue(existingValue),
                ["newValue"] = PropertyReader.FormatValue(newValue),
                ["type"] = newValue?.GetType().Name,
                ["scope"] = scope,
                ["applied"] = applied
            }, RakaJson.Options)
        };
    }

    private static object? ConvertResourceValue(string valueStr, object? existingValue)
    {
        if (existingValue == null) return valueStr;

        return existingValue switch
        {
            Microsoft.UI.Xaml.Media.SolidColorBrush => PropertyWriter.ParseBrush(valueStr),
            Windows.UI.Color => PropertyWriter.ParseColor(valueStr),
            double => double.Parse(valueStr),
            int => int.Parse(valueStr),
            bool => bool.Parse(valueStr),
            Thickness => PropertyWriter.ParseThickness(valueStr),
            CornerRadius => PropertyWriter.ParseCornerRadius(valueStr),
            string => valueStr,
            _ => valueStr
        };
    }

    private static string GetDependencyPropertyName(DependencyProperty? dp, Type? targetType)
    {
        if (dp == null) return "(unknown)";

        // Search the target type's static properties for a match
        var searchTypes = new List<Type>();
        if (targetType != null) searchTypes.Add(targetType);
        // Also search common base types
        searchTypes.AddRange(new[] { typeof(FrameworkElement), typeof(UIElement), typeof(Control) });

        foreach (var type in searchTypes)
        {
            var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy)
                .Where(p => p.PropertyType == typeof(DependencyProperty));
            foreach (var prop in props)
            {
                try
                {
                    if (ReferenceEquals(prop.GetValue(null), dp))
                    {
                        var name = prop.Name;
                        return name.EndsWith("Property") ? name[..^8] : name;
                    }
                }
                catch { }
            }
        }

        return dp.ToString() ?? "(unknown)";
    }
}
