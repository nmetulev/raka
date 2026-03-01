using System.Reflection;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Raka.DevTools.Core;

/// <summary>
/// React-style XAML tree reconciler. Instead of replacing the entire visual tree on
/// hot-reload, diffs old vs new XAML and applies only the changes to the live tree.
/// This preserves runtime state (toggle values, text input, scroll position) on
/// elements that weren't structurally changed.
/// </summary>
internal sealed class XamlReconciler
{
    private readonly Dictionary<string, string> _lastXaml = new(StringComparer.Ordinal);

    private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace DefaultNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>
    /// Attempts to reconcile a live element tree with new XAML using minimal patches.
    /// Returns (true, patchCount) on success, (false, 0) if full replacement is needed.
    /// </summary>
    public (bool success, int patches, string? error) TryReconcile(DependencyObject liveRoot, string elementId, string newXaml)
    {
        if (!_lastXaml.TryGetValue(elementId, out var oldXaml))
        {
            _lastXaml[elementId] = newXaml;
            return (false, 0, "no_cache"); // first time — no old XAML to diff against
        }

        _lastXaml[elementId] = newXaml;

        if (oldXaml == newXaml) return (true, 0, null); // no change

        try
        {
            var oldDoc = XDocument.Parse(EnsureXmlns(oldXaml));
            var newDoc = XDocument.Parse(EnsureXmlns(newXaml));

            int patches = 0;
            ReconcileNode(liveRoot, oldDoc.Root!, newDoc.Root!, ref patches);
            return (true, patches, null);
        }
        catch (Exception ex)
        {
            // Any failure in reconciliation → caller does full replacement
            return (false, 0, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Caches XAML for an element (call after a full replacement).</summary>
    public void CacheXaml(string elementId, string xaml)
    {
        _lastXaml[elementId] = xaml;
    }

    private void ReconcileNode(DependencyObject live, XElement oldXml, XElement newXml, ref int patches)
    {
        // If the element type changed, can't reconcile in place
        if (GetTypeName(oldXml) != GetTypeName(newXml))
            throw new InvalidOperationException("Type changed");

        // 1. Diff and patch attributes (properties)
        PatchAttributes(live, oldXml, newXml, ref patches);

        // 2. Diff and patch property elements (e.g., <Grid.RowDefinitions>)
        PatchPropertyElements(live, oldXml, newXml, ref patches);

        // 3. Diff and patch text content (e.g., <TextBlock>Hello</TextBlock>)
        PatchTextContent(live, oldXml, newXml, ref patches);

        // 4. Reconcile content children
        ReconcileChildren(live, oldXml, newXml, ref patches);
    }

    private void PatchAttributes(DependencyObject live, XElement oldXml, XElement newXml, ref int patches)
    {
        var oldAttrs = GetPropertyAttributes(oldXml);
        var newAttrs = GetPropertyAttributes(newXml);

        // Changed or added properties
        foreach (var (name, newVal) in newAttrs)
        {
            if (oldAttrs.TryGetValue(name, out var oldVal) && oldVal == newVal)
                continue; // unchanged

            if (TrySetPropertyFromString(live, name, newVal))
                patches++;
        }

        // Removed properties — clear to default
        foreach (var (name, _) in oldAttrs)
        {
            if (!newAttrs.ContainsKey(name))
            {
                if (TryClearProperty(live, name))
                    patches++;
            }
        }
    }

    private void PatchPropertyElements(DependencyObject live, XElement oldXml, XElement newXml, ref int patches)
    {
        // Property elements are children like <Grid.RowDefinitions>, <Button.Content>, etc.
        var typeName = GetTypeName(oldXml);
        var oldPropElems = GetPropertyElements(oldXml, typeName);
        var newPropElems = GetPropertyElements(newXml, typeName);

        foreach (var (propName, newElem) in newPropElems)
        {
            if (oldPropElems.TryGetValue(propName, out var oldElem))
            {
                // Compare the full XML — if different, try to apply
                if (oldElem.ToString() != newElem.ToString())
                {
                    // Property element changed. For collection properties (RowDefinitions, etc.)
                    // reconcile individual items. For single-value properties, set directly.
                    if (TryReconcilePropertyElement(live, propName, oldElem, newElem, ref patches))
                        continue;
                    // Can't reconcile property element → signal full replacement needed
                    throw new InvalidOperationException($"Property element {propName} changed");
                }
            }
            else
            {
                // New property element added — need full replacement
                throw new InvalidOperationException($"Property element {propName} added");
            }
        }

        // Check for removed property elements
        foreach (var propName in oldPropElems.Keys)
        {
            if (!newPropElems.ContainsKey(propName))
                throw new InvalidOperationException($"Property element {propName} removed");
        }
    }

    private void PatchTextContent(DependencyObject live, XElement oldXml, XElement newXml, ref int patches)
    {
        // Direct text content like <TextBlock>Hello</TextBlock>
        var oldText = GetDirectTextContent(oldXml);
        var newText = GetDirectTextContent(newXml);

        if (oldText == newText) return;
        if (newText == null) return;

        // Try to set Text property, then Content
        if (TrySetPropertyFromString(live, "Text", newText))
            patches++;
        else if (TrySetPropertyFromString(live, "Content", newText))
            patches++;
    }

    private void ReconcileChildren(DependencyObject live, XElement oldXml, XElement newXml, ref int patches)
    {
        var oldChildren = GetContentElements(oldXml);
        var newChildren = GetContentElements(newXml);
        var liveChildren = GetLiveContentChildren(live);

        if (oldChildren.Count != newChildren.Count)
        {
            // Structural change in children — try key-based matching
            if (!TryReconcileWithKeys(live, liveChildren, oldChildren, newChildren, ref patches))
                throw new InvalidOperationException("Child count changed without matching keys");
            return;
        }

        // Same count — match positionally, using keys to detect reorders
        int commonCount = Math.Min(oldChildren.Count, liveChildren.Count);

        for (int i = 0; i < commonCount; i++)
        {
            if (GetTypeName(oldChildren[i]) == GetTypeName(newChildren[i]))
            {
                // Same type at same position → reconcile in place (state preserved!)
                ReconcileNode(liveChildren[i], oldChildren[i], newChildren[i], ref patches);
            }
            else
            {
                // Type changed at this position → fall back
                throw new InvalidOperationException($"Child type changed at index {i}");
            }
        }
    }

    private bool TryReconcileWithKeys(
        DependencyObject parent,
        List<DependencyObject> liveChildren,
        List<XElement> oldChildren,
        List<XElement> newChildren,
        ref int patches)
    {
        // Build key maps (x:Name → index)
        var oldByKey = BuildKeyMap(oldChildren);
        var newByKey = BuildKeyMap(newChildren);

        // If any keyed element exists in both old and new, we can match them
        // For now, only handle simple add/remove at the end when keys match
        if (parent is not Panel panel) return false;

        // Try to match all new children to old children by key or position
        var matched = new List<(XElement oldElem, XElement newElem, DependencyObject liveElem)>();

        foreach (var newChild in newChildren)
        {
            var key = GetKey(newChild);
            if (key != null && oldByKey.TryGetValue(key, out var oldIdx) && oldIdx < liveChildren.Count)
            {
                matched.Add((oldChildren[oldIdx], newChild, liveChildren[oldIdx]));
            }
        }

        // Reconcile matched elements in place
        foreach (var (oldElem, newElem, liveElem) in matched)
        {
            if (GetTypeName(oldElem) == GetTypeName(newElem))
                ReconcileNode(liveElem, oldElem, newElem, ref patches);
        }

        // For added children: parse and add via XamlReader
        if (newChildren.Count > oldChildren.Count)
        {
            for (int i = oldChildren.Count; i < newChildren.Count; i++)
            {
                var childXaml = newChildren[i].ToString();
                var parsed = (DependencyObject)XamlReader.Load(childXaml);
                if (parsed is UIElement uie)
                {
                    panel.Children.Add(uie);
                    patches++;
                }
            }
        }

        // For removed children: remove from the end
        if (newChildren.Count < oldChildren.Count)
        {
            for (int i = oldChildren.Count - 1; i >= newChildren.Count; i--)
            {
                if (i < panel.Children.Count)
                {
                    panel.Children.RemoveAt(i);
                    patches++;
                }
            }
        }

        return true;
    }

    private bool TryReconcilePropertyElement(
        DependencyObject live, string propName,
        XElement oldPropElem, XElement newPropElem, ref int patches)
    {
        // Handle collection property elements like RowDefinitions/ColumnDefinitions
        var oldItems = oldPropElem.Elements().ToList();
        var newItems = newPropElem.Elements().ToList();

        if (oldItems.Count != newItems.Count) return false;

        // Get the collection from the live object
        var collProp = live.GetType().GetProperty(propName);
        if (collProp == null) return false;

        var collection = collProp.GetValue(live);
        if (collection is not System.Collections.IList list) return false;

        // Reconcile each item in the collection
        for (int i = 0; i < oldItems.Count && i < list.Count; i++)
        {
            if (GetTypeName(oldItems[i]) != GetTypeName(newItems[i])) return false;
            if (list[i] is DependencyObject depObj)
                PatchAttributes(depObj, oldItems[i], newItems[i], ref patches);
        }

        return true;
    }

    // ── Property setting ──────────────────────────────────────────────

    private bool TrySetPropertyFromString(DependencyObject obj, string propertyName, string value)
    {
        try
        {
            // Skip markup extensions — can't resolve these without resource context
            if (value.StartsWith('{')) return false;

            // Handle attached properties (e.g., "Grid.Row")
            if (propertyName.Contains('.'))
                return TrySetAttachedProperty(obj, propertyName, value);

            var type = obj.GetType();
            var dp = FindDependencyProperty(type, propertyName);
            if (dp == null) return false;

            var clrProp = type.GetProperty(propertyName);
            if (clrProp == null) return false;

            var converted = ConvertValue(clrProp.PropertyType, value);
            if (converted == null) return false;

            obj.SetValue(dp, converted);
            return true;
        }
        catch { return false; }
    }

    private static bool TrySetAttachedProperty(DependencyObject obj, string fullName, string value)
    {
        try
        {
            var dotIdx = fullName.IndexOf('.');
            var ownerName = fullName[..dotIdx];
            var propName = fullName[(dotIdx + 1)..];

            // Resolve owner type from WinUI assemblies
            var ownerType = ResolveType(ownerName);
            if (ownerType == null) return false;

            var dp = FindDependencyProperty(ownerType, propName);
            if (dp == null) return false;

            // Find the Set method to determine target type
            var setter = ownerType.GetMethod($"Set{propName}", BindingFlags.Public | BindingFlags.Static);
            if (setter == null) return false;

            var paramType = setter.GetParameters().LastOrDefault()?.ParameterType ?? typeof(object);
            var converted = ConvertValue(paramType, value);
            if (converted == null) return false;

            obj.SetValue(dp, converted);
            return true;
        }
        catch { return false; }
    }

    private static bool TryClearProperty(DependencyObject obj, string propertyName)
    {
        try
        {
            if (propertyName.Contains('.'))
            {
                var dotIdx = propertyName.IndexOf('.');
                var ownerType = ResolveType(propertyName[..dotIdx]);
                if (ownerType == null) return false;
                var dp = FindDependencyProperty(ownerType, propertyName[(dotIdx + 1)..]);
                if (dp == null) return false;
                obj.ClearValue(dp);
                return true;
            }

            var dp2 = FindDependencyProperty(obj.GetType(), propertyName);
            if (dp2 == null) return false;
            obj.ClearValue(dp2);
            return true;
        }
        catch { return false; }
    }

    // ── Type and property resolution ──────────────────────────────────

    private static DependencyProperty? FindDependencyProperty(Type type, string propertyName)
    {
        var name = propertyName + "Property";
        // WinUI 3 (C#/WinRT projection) exposes DependencyProperty as static properties, not fields
        var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (prop?.GetValue(null) is DependencyProperty dp) return dp;
        // Fallback: try field (in case of custom controls)
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        return field?.GetValue(null) as DependencyProperty;
    }

    private static Type? ResolveType(string shortName)
    {
        // Search WinUI Controls assembly
        var asm = typeof(FrameworkElement).Assembly;
        return asm.GetTypes().FirstOrDefault(t => t.Name == shortName)
            ?? typeof(Grid).Assembly.GetTypes().FirstOrDefault(t => t.Name == shortName);
    }

    private static object? ConvertValue(Type targetType, string value)
    {
        try
        {
            return XamlBindingHelper.ConvertValue(targetType, value);
        }
        catch
        {
            // Fallback conversions
            if (targetType == typeof(string)) return value;
            if (targetType == typeof(double) && double.TryParse(value, out var d)) return d;
            if (targetType == typeof(int) && int.TryParse(value, out var i)) return i;
            if (targetType == typeof(bool) && bool.TryParse(value, out var b)) return b;
            if (targetType == typeof(float) && float.TryParse(value, out var f)) return f;
            if (targetType.IsEnum) return Enum.Parse(targetType, value);
            return null;
        }
    }

    // ── XML helpers ───────────────────────────────────────────────────

    private static string GetTypeName(XElement elem)
    {
        return elem.Name.LocalName;
    }

    private static string? GetKey(XElement elem)
    {
        return elem.Attribute(XamlNs + "Name")?.Value
            ?? elem.Attribute("Name")?.Value;
    }

    private static Dictionary<string, string> GetPropertyAttributes(XElement elem)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attr in elem.Attributes())
        {
            if (attr.IsNamespaceDeclaration) continue;
            // Skip x: directives (x:Name, x:Class, x:Key, x:Uid)
            if (attr.Name.Namespace == XamlNs) continue;
            var name = attr.Name.LocalName;
            result[name] = attr.Value;
        }
        return result;
    }

    private static List<XElement> GetContentElements(XElement parent)
    {
        // Content children are child elements that are NOT property elements.
        // Property elements have names like "Grid.RowDefinitions" (contain a dot).
        return parent.Elements()
            .Where(e => !e.Name.LocalName.Contains('.'))
            .ToList();
    }

    private static Dictionary<string, XElement> GetPropertyElements(XElement parent, string typeName)
    {
        // Property elements are children like <Grid.RowDefinitions>
        var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var child in parent.Elements())
        {
            var localName = child.Name.LocalName;
            if (localName.StartsWith(typeName + "."))
            {
                var propName = localName[(typeName.Length + 1)..];
                result[propName] = child;
            }
        }
        return result;
    }

    private static string? GetDirectTextContent(XElement elem)
    {
        // Gets direct text node content (not from child elements)
        var textNodes = elem.Nodes().OfType<XText>();
        var text = string.Concat(textNodes.Select(t => t.Value)).Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static List<DependencyObject> GetLiveContentChildren(DependencyObject parent)
    {
        if (parent is Panel panel)
            return panel.Children.Cast<DependencyObject>().ToList();
        if (parent is ContentControl cc && cc.Content is UIElement ccChild)
            return [ccChild];
        if (parent is Border border && border.Child != null)
            return [border.Child];
        if (parent is Viewbox vb && vb.Child != null)
            return [vb.Child];
        if (parent is ContentPresenter cp && cp.Content is UIElement cpChild)
            return [cpChild];
        // Generic Content property fallback
        var contentProp = parent.GetType().GetProperty("Content");
        if (contentProp?.GetValue(parent) is DependencyObject dpContent)
            return [dpContent];
        return [];
    }

    private static Dictionary<string, int> BuildKeyMap(List<XElement> elements)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < elements.Count; i++)
        {
            var key = GetKey(elements[i]);
            if (key != null) map[key] = i;
        }
        return map;
    }

    private static string EnsureXmlns(string xaml)
    {
        if (!xaml.Contains("xmlns="))
        {
            var firstGt = xaml.IndexOf('>');
            var firstSpace = xaml.IndexOf(' ');
            int insertPos = (firstSpace > 0 && firstSpace < firstGt) ? firstSpace :
                            (firstGt > 0) ? firstGt : throw new ArgumentException("Invalid XAML");

            xaml = xaml.Insert(insertPos,
                " xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"" +
                " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"");
        }
        return xaml;
    }
}
