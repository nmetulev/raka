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

        if (oldXaml == newXaml) return (true, 0, null); // no change

        try
        {
            var oldDoc = XDocument.Parse(EnsureXmlns(oldXaml));
            var newDoc = XDocument.Parse(EnsureXmlns(newXaml));

            int patches = 0;
            ReconcileNode(liveRoot, oldDoc.Root!, newDoc.Root!, ref patches);
            _lastXaml[elementId] = newXaml; // only cache after successful reconciliation
            return (true, patches, null);
        }
        catch (Exception ex)
        {
            // Don't update cache — caller will cache after successful full replacement
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

        if (oldChildren.Count == 0 && newChildren.Count == 0)
            return;

        // Build identity maps: key → index in old/live list
        var oldByKey = BuildKeyMap(oldChildren);

        // Phase 1: Match each new child to an old/live child using multi-factor identity
        //   Priority: key match > positional match (same type at same index)
        var matches = new (int oldIdx, int newIdx, DependencyObject liveElem)[newChildren.Count];
        var usedOld = new HashSet<int>();

        for (int ni = 0; ni < newChildren.Count; ni++)
        {
            matches[ni] = (-1, ni, null!); // default: unmatched

            // 1a. Try key-based match (x:Name, AutomationId, x:Uid)
            var key = GetKey(newChildren[ni]);
            if (key != null && oldByKey.TryGetValue(key, out var oi) && oi < liveChildren.Count && !usedOld.Contains(oi))
            {
                matches[ni] = (oi, ni, liveChildren[oi]);
                usedOld.Add(oi);
                continue;
            }

            // 1b. Try positional match (same index, same type)
            if (ni < oldChildren.Count && ni < liveChildren.Count && !usedOld.Contains(ni)
                && GetTypeName(oldChildren[ni]) == GetTypeName(newChildren[ni]))
            {
                matches[ni] = (ni, ni, liveChildren[ni]);
                usedOld.Add(ni);
            }
        }

        // Phase 2: For still-unmatched new children, try signature matching then type fallback
        for (int ni = 0; ni < matches.Length; ni++)
        {
            if (matches[ni].oldIdx >= 0) continue; // already matched

            var newType = GetTypeName(newChildren[ni]);
            var newSig = GetSignature(newChildren[ni]);

            // 2a. Signature match: same type + matching stable property fingerprint
            if (newSig != null)
            {
                for (int oi = 0; oi < oldChildren.Count && oi < liveChildren.Count; oi++)
                {
                    if (!usedOld.Contains(oi) && GetTypeName(oldChildren[oi]) == newType
                        && GetSignature(oldChildren[oi]) == newSig)
                    {
                        matches[ni] = (oi, ni, liveChildren[oi]);
                        usedOld.Add(oi);
                        break;
                    }
                }
                if (matches[ni].oldIdx >= 0) continue;
            }

            // 2b. Type-only fallback: any unused old child of the same type
            for (int oi = 0; oi < oldChildren.Count && oi < liveChildren.Count; oi++)
            {
                if (!usedOld.Contains(oi) && GetTypeName(oldChildren[oi]) == newType)
                {
                    matches[ni] = (oi, ni, liveChildren[oi]);
                    usedOld.Add(oi);
                    break;
                }
            }
        }

        // Phase 3: Apply — reconcile matched, create unmatched, remove orphans
        var panel = live as Panel;
        var resultChildren = new List<UIElement>();

        for (int ni = 0; ni < matches.Length; ni++)
        {
            var (oldIdx, _, liveElem) = matches[ni];

            if (oldIdx >= 0 && GetTypeName(oldChildren[oldIdx]) == GetTypeName(newChildren[ni]))
            {
                // Matched + same type → reconcile in place (state preserved!)
                try
                {
                    ReconcileNode(liveElem, oldChildren[oldIdx], newChildren[ni], ref patches);
                    if (liveElem is UIElement uie)
                        resultChildren.Add(uie);
                    continue;
                }
                catch
                {
                    // Reconciliation of this subtree failed — fall through to recreate
                }
            }

            // Unmatched or type changed or reconciliation failed → create new element
            if (panel != null)
            {
                var childXaml = EnsureXmlns(newChildren[ni].ToString());
                var parsed = XamlReader.Load(childXaml);
                if (parsed is UIElement newUie)
                {
                    resultChildren.Add(newUie);
                    patches++;
                    continue;
                }

                // XamlReader produced a non-UIElement — can't reconcile
                throw new InvalidOperationException(
                    $"XamlReader.Load produced {parsed?.GetType().Name ?? "null"} for {GetTypeName(newChildren[ni])}, expected UIElement");
            }

            // Non-panel parent — keep old element if we had a match, otherwise fail
            if (oldIdx >= 0 && liveElem is UIElement fallbackUie)
                resultChildren.Add(fallbackUie);
            else
                throw new InvalidOperationException(
                    $"Cannot create child {GetTypeName(newChildren[ni])} in {live.GetType().Name}");
        }

        // Phase 4: Replace panel children if the list changed
        if (panel != null && !ChildListEqual(panel.Children, resultChildren))
        {
            // Detach all, then re-add in new order
            panel.Children.Clear();
            foreach (var child in resultChildren)
                panel.Children.Add(child);
            patches++;
        }
    }

    private static bool ChildListEqual(UIElementCollection current, List<UIElement> expected)
    {
        if (current.Count != expected.Count) return false;
        for (int i = 0; i < current.Count; i++)
        {
            if (!ReferenceEquals(current[i], expected[i])) return false;
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

    // Stable properties used as identity fingerprints for unkeyed elements.
    // These properties typically identify *which* element it is (not how it looks).
    private static readonly string[] SignatureProperties =
        ["Text", "Content", "Header", "Source", "Glyph", "NavigateUri", "Tag", "Label", "Title", "PlaceholderText"];

    private static string GetTypeName(XElement elem)
    {
        return elem.Name.LocalName;
    }

    /// <summary>
    /// Builds a fingerprint from stable identity properties for matching
    /// unkeyed elements that may have shifted position.
    /// </summary>
    private static string? GetSignature(XElement elem)
    {
        // Collect identity property values that are present on this element
        string? best = null;
        foreach (var prop in SignatureProperties)
        {
            var val = elem.Attribute(prop)?.Value;
            if (val != null && !val.StartsWith('{'))
            {
                // Use the first stable property found as signature.
                // Concatenate type + property for uniqueness.
                best = $"{elem.Name.LocalName}:{prop}={val}";
                break;
            }
        }
        return best;
    }

    private static string? GetKey(XElement elem)
    {
        // Multi-factor identity: x:Name > AutomationProperties.AutomationId > x:Uid > Name
        return elem.Attribute(XamlNs + "Name")?.Value
            ?? elem.Attribute("AutomationProperties.AutomationId")?.Value
            ?? elem.Attribute(XamlNs + "Uid")?.Value
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
