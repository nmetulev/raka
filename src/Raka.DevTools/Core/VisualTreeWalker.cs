using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using Raka.Protocol;

namespace Raka.DevTools.Core;

/// <summary>
/// Walks the WinUI 3 visual tree using VisualTreeHelper.
/// Must be called on the UI thread.
/// </summary>
internal sealed class VisualTreeWalker
{
    private readonly Dictionary<int, DependencyObject> _elementMap = new();
    private int _nextId;

    /// <summary>
    /// Gets an element by its assigned ID (e.g., "e0", "e5").
    /// </summary>
    public DependencyObject? GetElement(string elementId)
    {
        if (elementId.StartsWith('e') && int.TryParse(elementId[1..], out var index))
        {
            _elementMap.TryGetValue(index, out var element);
            return element;
        }
        return null;
    }

    /// <summary>
    /// Walks the visual tree starting from the given root, up to the specified depth.
    /// </summary>
    public ElementNode Walk(DependencyObject root, int maxDepth = int.MaxValue)
    {
        _elementMap.Clear();
        _nextId = 0;
        return WalkElement(root, 0, maxDepth);
    }

    /// <summary>
    /// Walks a subtree from a specific element.
    /// </summary>
    public ElementNode WalkFrom(DependencyObject element, int maxDepth = int.MaxValue)
    {
        return WalkElement(element, 0, maxDepth);
    }

    /// <summary>
    /// Searches the visual tree for elements matching the given criteria.
    /// </summary>
    public List<ElementNode> Search(DependencyObject root, string? type, string? name, string? text, string? automationId,
        string? className = null, bool interactive = false, bool visibleOnly = false, string? property = null)
    {
        var results = new List<ElementNode>();
        SearchRecursive(root, type, name, text, automationId, className, interactive, visibleOnly, property, results);
        return results;
    }

    /// <summary>
    /// Returns the ancestor chain from an element to the root.
    /// </summary>
    public List<ElementNode> GetAncestors(DependencyObject element)
    {
        var ancestors = new List<ElementNode>();
        var current = VisualTreeHelper.GetParent(element);
        while (current != null)
        {
            ancestors.Add(CreateNode(current, includeChildren: false));
            current = VisualTreeHelper.GetParent(current);
        }
        return ancestors;
    }

    private ElementNode WalkElement(DependencyObject obj, int depth, int maxDepth)
    {
        var node = CreateNode(obj, includeChildren: depth < maxDepth);

        if (depth < maxDepth)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(obj);
            if (childCount > 0)
            {
                node.Children = new List<ElementNode>(childCount);
                for (int i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(obj, i);
                    node.Children.Add(WalkElement(child, depth + 1, maxDepth));
                }
            }
        }

        return node;
    }

    private ElementNode CreateNode(DependencyObject obj, bool includeChildren)
    {
        var id = _nextId++;
        _elementMap[id] = obj;

        var typeName = obj.GetType().Name;
        var fullTypeName = obj.GetType().FullName ?? typeName;

        string? name = null;
        string? autoId = null;
        string? visibility = null;
        ElementBounds? bounds = null;
        int childCount = 0;

        if (obj is FrameworkElement fe)
        {
            name = string.IsNullOrEmpty(fe.Name) ? null : fe.Name;
            autoId = AutomationProperties.GetAutomationId(fe);
            if (string.IsNullOrEmpty(autoId)) autoId = null;
            visibility = fe.Visibility.ToString();

            try
            {
                var transform = fe.TransformToVisual(null);
                var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                bounds = new ElementBounds
                {
                    X = Math.Round(point.X, 1),
                    Y = Math.Round(point.Y, 1),
                    Width = Math.Round(fe.ActualWidth, 1),
                    Height = Math.Round(fe.ActualHeight, 1)
                };
            }
            catch
            {
                // Element may not be in the visual tree yet
            }
        }

        childCount = VisualTreeHelper.GetChildrenCount(obj);

        // For Frame elements, expose the Content type (Pages aren't in the visual tree)
        string? contentClassName = null;
        if (obj is Microsoft.UI.Xaml.Controls.Frame frame && frame.Content != null)
        {
            contentClassName = frame.Content.GetType().FullName;
        }

        return new ElementNode
        {
            Id = $"e{id}",
            Type = typeName,
            ClassName = fullTypeName,
            Name = name,
            AutomationId = autoId,
            Visibility = visibility,
            Bounds = bounds,
            ChildCount = childCount,
            ContentClassName = contentClassName
        };
    }

    private void SearchRecursive(DependencyObject obj, string? type, string? name, string? text, string? automationId,
        string? className, bool interactive, bool visibleOnly, string? property, List<ElementNode> results)
    {
        bool matches = true;

        if (type != null)
        {
            var typeName = obj.GetType().Name;
            matches = typeName.Equals(type, StringComparison.OrdinalIgnoreCase);
        }

        if (matches && className != null)
        {
            var fullName = obj.GetType().FullName ?? obj.GetType().Name;
            matches = fullName.Contains(className, StringComparison.OrdinalIgnoreCase);
        }

        if (matches && name != null && obj is FrameworkElement fe1)
        {
            matches = fe1.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true;
        }
        else if (matches && name != null)
        {
            matches = false;
        }

        if (matches && automationId != null && obj is FrameworkElement fe2)
        {
            var autoId = AutomationProperties.GetAutomationId(fe2);
            matches = autoId?.Equals(automationId, StringComparison.OrdinalIgnoreCase) == true;
        }
        else if (matches && automationId != null)
        {
            matches = false;
        }

        // Text search — check common text properties
        if (matches && text != null)
        {
            matches = HasTextContent(obj, text);
        }

        // Visible-only filter
        if (matches && visibleOnly && obj is FrameworkElement feVis)
        {
            matches = feVis.Visibility == Visibility.Visible;
        }
        else if (matches && visibleOnly && obj is not FrameworkElement)
        {
            matches = false;
        }

        // Interactive filter — only elements with invoke, toggle, or select automation patterns
        if (matches && interactive && obj is UIElement uiEl)
        {
            matches = IsInteractive(uiEl);
        }
        else if (matches && interactive && obj is not UIElement)
        {
            matches = false;
        }

        // Property filter (e.g., "Tag=dashboard", "IsEnabled=True")
        if (matches && property != null)
        {
            matches = MatchesProperty(obj, property);
        }

        if (matches)
        {
            results.Add(CreateNode(obj, includeChildren: false));
        }

        int childCount = VisualTreeHelper.GetChildrenCount(obj);
        for (int i = 0; i < childCount; i++)
        {
            SearchRecursive(VisualTreeHelper.GetChild(obj, i), type, name, text, automationId, className, interactive, visibleOnly, property, results);
        }
    }

    private static bool HasTextContent(DependencyObject obj, string text)
    {
        // Check common text-bearing properties via reflection
        var type = obj.GetType();
        var textProp = type.GetProperty("Text");
        if (textProp != null)
        {
            var val = textProp.GetValue(obj)?.ToString();
            if (val != null && val.Contains(text, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var contentProp = type.GetProperty("Content");
        if (contentProp != null)
        {
            var val = contentProp.GetValue(obj)?.ToString();
            if (val != null && val.Contains(text, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var headerProp = type.GetProperty("Header");
        if (headerProp != null)
        {
            var val = headerProp.GetValue(obj)?.ToString();
            if (val != null && val.Contains(text, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsInteractive(UIElement element)
    {
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(element);
        if (peer == null) return false;
        return peer.GetPattern(PatternInterface.Invoke) != null
            || peer.GetPattern(PatternInterface.Toggle) != null
            || peer.GetPattern(PatternInterface.SelectionItem) != null
            || peer.GetPattern(PatternInterface.ExpandCollapse) != null
            || peer.GetPattern(PatternInterface.Value) != null;
    }

    private static bool MatchesProperty(DependencyObject obj, string propertyFilter)
    {
        // Format: "PropertyName=Value" (e.g., "Tag=dashboard", "IsEnabled=True")
        var eqIndex = propertyFilter.IndexOf('=');
        if (eqIndex < 1) return false;

        var propName = propertyFilter[..eqIndex];
        var expected = propertyFilter[(eqIndex + 1)..];

        var prop = obj.GetType().GetProperty(propName);
        if (prop == null) return false;

        var val = prop.GetValue(obj)?.ToString();
        return val != null && val.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
