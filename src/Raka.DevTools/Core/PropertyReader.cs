using System.Reflection;
using Microsoft.UI.Xaml;
using Raka.Protocol;

namespace Raka.DevTools.Core;

/// <summary>
/// Reads DependencyProperty values from XAML elements.
/// Must be called on the UI thread.
/// </summary>
internal static class PropertyReader
{
    /// <summary>
    /// Reads a single named property from an element.
    /// </summary>
    public static PropertyValue? ReadProperty(DependencyObject element, string propertyName)
    {
        var dp = FindDependencyProperty(element, propertyName);
        if (dp != null)
        {
            var value = element.GetValue(dp);
            return new PropertyValue
            {
                Name = propertyName,
                Value = FormatValue(value),
                Type = dp.GetType().Name,
                Source = GetPropertySource(element, dp)
            };
        }

        // Fall back to CLR property via reflection
        var prop = element.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop != null)
        {
            var value = prop.GetValue(element);
            return new PropertyValue
            {
                Name = propertyName,
                Value = FormatValue(value),
                Type = prop.PropertyType.Name,
                Source = "CLR"
            };
        }

        return null;
    }

    /// <summary>
    /// Reads all available DependencyProperties from an element.
    /// </summary>
    public static List<PropertyValue> ReadAllProperties(DependencyObject element)
    {
        var results = new List<PropertyValue>();
        var type = element.GetType();

        // In WinUI 3 (CsWinRT), DependencyProperty values are exposed as static properties, not fields
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(p => p.PropertyType == typeof(DependencyProperty));

        foreach (var prop in props)
        {
            try
            {
                var dp = (DependencyProperty?)prop.GetValue(null);
                if (dp == null) continue;

                var name = prop.Name;
                if (name.EndsWith("Property"))
                    name = name[..^8];

                var value = element.GetValue(dp);
                results.Add(new PropertyValue
                {
                    Name = name,
                    Value = FormatValue(value),
                    Type = value?.GetType().Name ?? "null",
                    Source = GetPropertySource(element, dp)
                });
            }
            catch
            {
                // Some properties may throw when accessed
            }
        }

        return results.OrderBy(p => p.Name).ToList();
    }

    private static DependencyProperty? FindDependencyProperty(DependencyObject element, string propertyName)
    {
        var propName = propertyName.EndsWith("Property") ? propertyName : $"{propertyName}Property";
        // CsWinRT exposes DependencyProperty as static properties, not fields
        var prop = element.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (prop != null) return prop.GetValue(null) as DependencyProperty;
        // Also check fields as fallback
        var field = element.GetType().GetField(propName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        return field?.GetValue(null) as DependencyProperty;
    }

    private static string? GetPropertySource(DependencyObject element, DependencyProperty dp)
    {
        // WinUI 3 doesn't expose DependencyPropertyHelper like WPF
        // We can check if the value differs from the default
        try
        {
            var localValue = element.ReadLocalValue(dp);
            if (localValue == DependencyProperty.UnsetValue)
                return "Default";
            return "Local";
        }
        catch
        {
            return null;
        }
    }

    internal static string? FormatValue(object? value)
    {
        if (value == null) return null;

        return value switch
        {
            string s => s,
            bool b => b.ToString().ToLowerInvariant(),
            double d => d.ToString("G"),
            float f => f.ToString("G"),
            int i => i.ToString(),
            Microsoft.UI.Xaml.Thickness t => $"{t.Left},{t.Top},{t.Right},{t.Bottom}",
            Microsoft.UI.Xaml.CornerRadius cr => $"{cr.TopLeft},{cr.TopRight},{cr.BottomRight},{cr.BottomLeft}",
            Microsoft.UI.Xaml.GridLength gl => gl.IsAuto ? "Auto" : gl.IsStar ? $"{gl.Value}*" : gl.Value.ToString("G"),
            Windows.Foundation.Size s => $"{s.Width},{s.Height}",
            Windows.Foundation.Point p => $"{p.X},{p.Y}",
            _ => value.ToString()
        };
    }
}
