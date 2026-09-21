using System.Collections.ObjectModel;

namespace Haven.CUI.DevTools;

public readonly record struct ElementId
{
    public ElementId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct CuiPoint(double X, double Y);

public readonly record struct CuiSize(double Width, double Height);

public readonly record struct CuiRect(double X, double Y, double Width, double Height)
{
    public bool Contains(CuiPoint point) =>
        point.X >= X && point.X <= X + Width && point.Y >= Y && point.Y <= Y + Height;
}

public readonly record struct CuiThickness(double Left, double Top, double Right, double Bottom);

internal static class DiagnosticsCollections
{
    public static IReadOnlyList<T> Copy<T>(IEnumerable<T>? items) =>
        Array.AsReadOnly(items?.ToArray() ?? []);

    public static IReadOnlyDictionary<TKey, TValue> Copy<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>>? items,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, TValue>(comparer);
        if (items is not null)
        {
            foreach (var item in items)
                result[item.Key] = item.Value;
        }

        return new ReadOnlyDictionary<TKey, TValue>(result);
    }
}
