using System.Collections.Frozen;
using System.Text.Json.Serialization;

namespace Haven.Application;

/// <summary>A request can only remove currently observed concrete tool definitions.
/// The names grant no catalogue, resource, Home permission or canonical action authority.
/// The optional requirement is durable; a restored request intersects fresh maintained tools.</summary>
public sealed class ModelRequestToolSelectionConstraints : IEquatable<ModelRequestToolSelectionConstraints>
{
    private readonly FrozenSet<string> _allowed;

    [JsonConstructor]
    public ModelRequestToolSelectionConstraints(IReadOnlyList<string> allowedToolNames)
    {
        ArgumentNullException.ThrowIfNull(allowedToolNames);
        if (allowedToolNames.Count > 1024)
            throw new ArgumentOutOfRangeException(nameof(allowedToolNames), "A concrete selection must be bounded.");
        var original = allowedToolNames.ToArray();
        if (original.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 512))
            throw new ArgumentException("Retain bounded exact concrete tool names.", nameof(allowedToolNames));
        var captured = original.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        AllowedToolNames = Array.AsReadOnly(captured);
        _allowed = captured.ToFrozenSet(StringComparer.Ordinal);
    }

    public IReadOnlyList<string> AllowedToolNames { get; }
    public bool Allows(string exactToolName) => _allowed.Contains(exactToolName);

    public bool Equals(ModelRequestToolSelectionConstraints? other) => other is not null &&
        AllowedToolNames.SequenceEqual(other.AllowedToolNames, StringComparer.Ordinal);
    public override bool Equals(object? other) => other is ModelRequestToolSelectionConstraints actual && Equals(actual);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var name in AllowedToolNames) hash.Add(name, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
