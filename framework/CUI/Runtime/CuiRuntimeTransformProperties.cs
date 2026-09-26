using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Globalization;

namespace CakeOS.Cui.Runtime;

/// <summary>Composes authored transform components without one property replacing another.</summary>
public static class CuiRuntimeTransformProperties
{
    private static readonly AttachedProperty<Dictionary<string, string>?> ValuesProperty =
        AvaloniaProperty.RegisterAttached<Control, Dictionary<string, string>?>("CuiTransformValues", typeof(CuiRuntimeTransformProperties));

    public static void Set(Control control, string propertyName, string value)
    {
        var values = control.GetValue(ValuesProperty);
        if (values is null)
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            control.SetValue(ValuesProperty, values);
        }

        values[propertyName] = value;
        control.RenderTransform = CreateTransform(values);
    }

    private static Transform CreateTransform(IReadOnlyDictionary<string, string> values)
    {
        var transforms = new TransformGroup();

        var scale = ReadPair(values, "Scale", 1);
        transforms.Children.Add(new ScaleTransform(
            ReadNumber(values, "ScaleX", scale.X), ReadNumber(values, "ScaleY", scale.Y)));

        var skew = ReadPair(values, "Skew", 0);
        var skewX = ReadNumber(values, "SkewX", skew.X);
        var skewY = ReadNumber(values, "SkewY", skew.Y);
        if (skewX != 0 || skewY != 0)
            transforms.Children.Add(new SkewTransform(skewX, skewY));

        var rotation = ReadNumber(values, "Rotate", 0);
        if (rotation != 0)
            transforms.Children.Add(new RotateTransform(rotation));

        var translation = ReadPair(values, "Translate", 0);
        var translateX = ReadNumber(values, "TranslateX", translation.X);
        var translateY = ReadNumber(values, "TranslateY", translation.Y);
        if (translateX != 0 || translateY != 0)
            transforms.Children.Add(new TranslateTransform(translateX, translateY));

        return transforms;
    }

    private static (double X, double Y) ReadPair(IReadOnlyDictionary<string, string> values, string key, double fallback)
    {
        if (!values.TryGetValue(key, out var raw))
            return (fallback, fallback);
        var parts = raw.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && TryNumber(parts[0], out var single))
            return (single, single);
        if (parts.Length == 2 && TryNumber(parts[0], out var x) && TryNumber(parts[1], out var y))
            return (x, y);
        return (fallback, fallback);
    }

    private static double ReadNumber(IReadOnlyDictionary<string, string> values, string key, double fallback) =>
        values.TryGetValue(key, out var raw) && TryNumber(raw, out var parsed) ? parsed : fallback;

    private static bool TryNumber(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}
