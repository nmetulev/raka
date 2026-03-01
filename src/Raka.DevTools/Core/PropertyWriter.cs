using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Raka.DevTools.Core;

/// <summary>
/// Sets DependencyProperty values on XAML elements with XAML-style value parsing.
/// Must be called on the UI thread.
/// </summary>
internal static class PropertyWriter
{
    /// <summary>
    /// Sets a named property on the element, parsing the string value into the appropriate type.
    /// </summary>
    public static void SetProperty(DependencyObject element, string propertyName, string value)
    {
        var dp = FindDependencyProperty(element, propertyName);
        if (dp != null)
        {
            var targetType = GetPropertyType(element, propertyName);
            var parsed = ParseValue(value, targetType);
            element.SetValue(dp, parsed);
            return;
        }

        // Fall back to CLR property
        var prop = element.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop != null && prop.CanWrite)
        {
            var parsed = ParseValue(value, prop.PropertyType);
            prop.SetValue(element, parsed);
            return;
        }

        throw new ArgumentException($"Property '{propertyName}' not found on {element.GetType().Name}");
    }

    private static DependencyProperty? FindDependencyProperty(DependencyObject element, string propertyName)
    {
        var propName = propertyName.EndsWith("Property") ? propertyName : $"{propertyName}Property";
        var prop = element.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (prop != null) return prop.GetValue(null) as DependencyProperty;
        var field = element.GetType().GetField(propName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        return field?.GetValue(null) as DependencyProperty;
    }

    private static Type? GetPropertyType(DependencyObject element, string propertyName)
    {
        // Try to find a CLR property with the same name to get the type
        var prop = element.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return prop?.PropertyType;
    }

    /// <summary>
    /// Parses a string value into the target type, supporting common XAML types.
    /// </summary>
    internal static object? ParseValue(string value, Type? targetType)
    {
        if (targetType == null)
            return value;

        if (targetType == typeof(string))
            return value;

        if (targetType == typeof(double))
            return double.Parse(value);

        if (targetType == typeof(float))
            return float.Parse(value);

        if (targetType == typeof(int))
            return int.Parse(value);

        if (targetType == typeof(bool))
            return bool.Parse(value);

        if (targetType == typeof(Thickness))
            return ParseThickness(value);

        if (targetType == typeof(CornerRadius))
            return ParseCornerRadius(value);

        if (targetType == typeof(GridLength))
            return ParseGridLength(value);

        if (targetType == typeof(Visibility))
            return Enum.Parse<Visibility>(value, ignoreCase: true);

        if (targetType == typeof(HorizontalAlignment))
            return Enum.Parse<HorizontalAlignment>(value, ignoreCase: true);

        if (targetType == typeof(VerticalAlignment))
            return Enum.Parse<VerticalAlignment>(value, ignoreCase: true);

        if (targetType == typeof(Brush) || targetType.IsAssignableTo(typeof(Brush)))
            return ParseBrush(value);

        if (targetType.IsEnum)
            return Enum.Parse(targetType, value, ignoreCase: true);

        // Last resort: try Convert
        return Convert.ChangeType(value, targetType);
    }

    internal static Thickness ParseThickness(string value)
    {
        var parts = value.Split(',', ' ').Select(s => double.Parse(s.Trim())).ToArray();
        return parts.Length switch
        {
            1 => new Thickness(parts[0]),
            2 => new Thickness(parts[0], parts[1], parts[0], parts[1]),
            4 => new Thickness(parts[0], parts[1], parts[2], parts[3]),
            _ => throw new ArgumentException($"Invalid Thickness: '{value}'. Use 1, 2, or 4 values.")
        };
    }

    internal static CornerRadius ParseCornerRadius(string value)
    {
        var parts = value.Split(',', ' ').Select(s => double.Parse(s.Trim())).ToArray();
        return parts.Length switch
        {
            1 => new CornerRadius(parts[0]),
            4 => new CornerRadius(parts[0], parts[1], parts[2], parts[3]),
            _ => throw new ArgumentException($"Invalid CornerRadius: '{value}'. Use 1 or 4 values.")
        };
    }

    private static GridLength ParseGridLength(string value)
    {
        if (value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return new GridLength(1, GridUnitType.Auto);
        if (value.EndsWith('*'))
        {
            var num = value.Length == 1 ? 1.0 : double.Parse(value[..^1]);
            return new GridLength(num, GridUnitType.Star);
        }
        return new GridLength(double.Parse(value), GridUnitType.Pixel);
    }

    internal static Brush ParseBrush(string value)
    {
        // Support hex color strings like #FF0000, #AARRGGBB
        var color = ParseColor(value);
        return new SolidColorBrush(color);
    }

    internal static Windows.UI.Color ParseColor(string value)
    {
        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            return hex.Length switch
            {
                6 => Windows.UI.Color.FromArgb(0xFF,
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16)),
                8 => Windows.UI.Color.FromArgb(
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16),
                    Convert.ToByte(hex[6..8], 16)),
                _ => throw new ArgumentException($"Invalid hex color: '{value}'")
            };
        }

        // Try named colors
        return value.ToLowerInvariant() switch
        {
            "red" => Windows.UI.Color.FromArgb(0xFF, 0xFF, 0, 0),
            "green" => Windows.UI.Color.FromArgb(0xFF, 0, 0x80, 0),
            "blue" => Windows.UI.Color.FromArgb(0xFF, 0, 0, 0xFF),
            "white" => Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
            "black" => Windows.UI.Color.FromArgb(0xFF, 0, 0, 0),
            "yellow" => Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0),
            "orange" => Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xA5, 0),
            "purple" => Windows.UI.Color.FromArgb(0xFF, 0x80, 0, 0x80),
            "gray" or "grey" => Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80),
            "transparent" => Windows.UI.Color.FromArgb(0, 0, 0, 0),
            _ => throw new ArgumentException($"Unknown color: '{value}'. Use hex (#RRGGBB) or named colors.")
        };
    }
}
