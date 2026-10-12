namespace CakeOS.Cui.Runtime;

/// <summary>The only CUI presentation cascade, ordered by the language contract.</summary>
public enum CuiPropertyLevel { BuiltIn, Theme, Definition, Instance, Region, Overlay }

public sealed record CuiEffectiveProperty(string Name, Type ValueType, object? Value, CuiPropertyLevel Level, string Source, long Order);

/// <summary>Typed per-element stored values. Temporary overlays never overwrite their underlying levels.</summary>
public sealed class CuiEffectivePropertyStore
{
    private sealed record Declaration(object? Value, CuiPropertyLevel Level, string Source, long Order, long Sequence);
    private sealed class Property(Type type)
    {
        public Type Type { get; } = type;
        public Dictionary<(CuiPropertyLevel Level, string Source), Declaration> Values { get; } = [];
    }
    private readonly Dictionary<string, Property> _properties = new(StringComparer.OrdinalIgnoreCase);
    private long _sequence;
    public event EventHandler<CuiEffectiveProperty>? Changed;

    public void Register(string name, Type valueType, object? builtInDefault)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentNullException.ThrowIfNull(valueType);
        if (_properties.TryGetValue(name, out var existing))
        {
            if (existing.Type != valueType) throw new InvalidOperationException($"CUI property '{name}' is already registered as {existing.Type.Name}.");
            return;
        }
        Validate(name, valueType, builtInDefault);
        var property = new Property(valueType);
        property.Values.Add((CuiPropertyLevel.BuiltIn, "CUI built-in default"), new(builtInDefault, CuiPropertyLevel.BuiltIn, "CUI built-in default", 0, ++_sequence));
        _properties.Add(name, property);
    }

    public bool Contains(string name) => _properties.ContainsKey(name);
    public void Set(string name, object? value, CuiPropertyLevel level, string source, long order = 0)
    {
        if (!_properties.TryGetValue(name, out var property)) throw new KeyNotFoundException($"CUI property '{name}' is not registered.");
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!Enum.IsDefined(level)) throw new ArgumentOutOfRangeException(nameof(level));
        Validate(name, property.Type, value);
        var key = (level, source);
        if (property.Values.TryGetValue(key, out var previous) && Equals(previous.Value, value) && previous.Order == order) return;
        var before = Resolve(name);
        property.Values[key] = new(value, level, source, order, previous?.Sequence ?? ++_sequence);
        Publish(name, before);
    }

    public bool Remove(string name, CuiPropertyLevel level, string source)
    {
        if (level == CuiPropertyLevel.BuiltIn) throw new InvalidOperationException("A CUI property must retain its built-in default.");
        if (!_properties.TryGetValue(name, out var property) || !property.Values.ContainsKey((level, source))) return false;
        var before = Resolve(name);
        var removed = property.Values.Remove((level, source));
        if (removed) Publish(name, before);
        return removed;
    }

    public void RemoveSource(CuiPropertyLevel level, string source)
    { foreach (var name in _properties.Keys.ToArray()) Remove(name, level, source); }

    public CuiEffectiveProperty Resolve(string name)
    {
        if (!_properties.TryGetValue(name, out var property)) throw new KeyNotFoundException($"CUI property '{name}' is not registered.");
        Declaration? winner = null;
        foreach (var candidate in property.Values.Values)
            if (winner is null || candidate.Level > winner.Level ||
                candidate.Level == winner.Level && (candidate.Order > winner.Order ||
                candidate.Order == winner.Order && candidate.Sequence > winner.Sequence))
                winner = candidate;
        // Register installs an undeletable built-in declaration, so a property
        // always has a winner. Avoid allocating sorting iterators on each read.
        return new(name, property.Type, winner!.Value, winner.Level, winner.Source, winner.Order);
    }

    public IReadOnlyList<CuiEffectiveProperty> Inspect() => _properties.Keys.Order(StringComparer.OrdinalIgnoreCase).Select(Resolve).ToArray();
    private void Publish(string name, CuiEffectiveProperty before)
    { var after = Resolve(name); if (after != before) Changed?.Invoke(this, after); }
    private static void Validate(string name, Type type, object? value)
    {
        if (value is null ? type.IsValueType && Nullable.GetUnderlyingType(type) is null : !type.IsInstanceOfType(value))
            throw new ArgumentException($"CUI property '{name}' requires {type.Name}, received {value?.GetType().Name ?? "null"}.", nameof(value));
        if (value is double number && !double.IsFinite(number) &&
            !(double.IsNaN(number) && (name.Equals("Width", StringComparison.OrdinalIgnoreCase) || name.Equals("Height", StringComparison.OrdinalIgnoreCase))) &&
            !(double.IsPositiveInfinity(number) && (name.Equals("MaxWidth", StringComparison.OrdinalIgnoreCase) || name.Equals("MaxHeight", StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException($"CUI property '{name}' must be finite.", nameof(value));
    }
}
